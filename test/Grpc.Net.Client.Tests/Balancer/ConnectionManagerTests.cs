#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

#if SUPPORT_LOAD_BALANCING
using System.Diagnostics;
using System.Net;
using System.Threading.Channels;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using ChannelState = Grpc.Net.Client.Balancer.ChannelState;

namespace Grpc.Net.Client.Tests.Balancer;

[TestFixture]
public class ConnectionManagerTests
{
    internal class TestBackoffPolicyFactory : IBackoffPolicyFactory
    {
        private readonly TimeSpan _backoff;

        public TestBackoffPolicyFactory() : this(TimeSpan.FromSeconds(20))
        {
        }

        public TestBackoffPolicyFactory(TimeSpan backoff)
        {
            _backoff = backoff;
        }

        public IBackoffPolicy Create()
        {
            return new TestBackoffPolicy(_backoff);
        }

        private class TestBackoffPolicy : IBackoffPolicy
        {
            private readonly TimeSpan _backoff;

            public TestBackoffPolicy(TimeSpan backoff)
            {
                _backoff = backoff;
            }

            public TimeSpan NextBackoff()
            {
                return _backoff;
            }
        }
    }

    [Test]
    public async Task PickAsync_ChannelStateChangesWithWaitForReady_WaitsForCorrectEndpoint()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(GetType());

        var resolver = new TestResolver(loggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var transportFactory = new TestSubchannelTransportFactory();
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory);
        clientChannel.ConfigureBalancer(c => new RoundRobinBalancer(c, loggerFactory));

        // Act
        var pickTask1 = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();

        await clientChannel.ConnectAsync(waitForReady: true, CancellationToken.None).DefaultTimeout();

        var result1 = await pickTask1.DefaultTimeout();

        // Assert
        Assert.AreEqual(new DnsEndPoint("localhost", 80), result1.Address!.EndPoint);

        logger.LogInformation("Updating resolve to have 80 and 81 addresses.");
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80),
            new BalancerAddress("localhost", 81)
        });

        logger.LogInformation("Wait for both subchannels to be ready.");
        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(logger, clientChannel, expectedCount: 2);

        // This needs to happen after both subchannels are ready so the Transports collection has two items in it.
        logger.LogInformation("Make subchannels not ready.");
        for (var i = 0; i < transportFactory.Transports.Count; i++)
        {
            transportFactory.Transports[i].UpdateState(ConnectivityState.TransientFailure);
        }

        logger.LogInformation("Wait for both subchannels to not be ready.");
        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(logger, clientChannel, expectedCount: 0);

        var pickTask2 = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask().DefaultTimeout();

        _ = LogPickComplete(pickTask2, logger);

        Assert.IsFalse(pickTask2.IsCompleted, "PickAsync should wait until an subchannel is ready.");

        logger.LogInformation("Setting to ready subchannel.");
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 82)
        });

        var result2 = await pickTask2.DefaultTimeout();
        Assert.AreEqual(new DnsEndPoint("localhost", 82), result2.Address!.EndPoint);

        static async Task LogPickComplete(Task<(Subchannel Subchannel, BalancerAddress Address, ISubchannelCallTracker? SubchannelCallTracker)> pickTask2, ILogger logger)
        {
            await pickTask2.DefaultTimeout();
            logger.LogInformation("PickAsync complete.");
        }
    }

    [Test]
    public async Task PickAsync_WaitForReadyWithDrop_ThrowsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var resolver = new TestResolver(loggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var transportFactory = new TestSubchannelTransportFactory();
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory);
        clientChannel.ConfigureBalancer(c => new DropLoadBalancer(c));

        // Act
        _ = clientChannel.ConnectAsync(waitForReady: true, CancellationToken.None).ConfigureAwait(false);

        var pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pickTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.DataLoss, ex.StatusCode);
    }

    [Test]
    public async Task PickAsync_ErrorConnectingToSubchannel_ThrowsError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var resolver = new TestResolver(loggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            return Task.FromException<TryConnectResult>(new Exception("Test error!"));
        });
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory);
        clientChannel.ConfigureBalancer(c => new PickFirstBalancer(c, loggerFactory));

        // Act
        _ = clientChannel.ConnectAsync(waitForReady: false, CancellationToken.None).ConfigureAwait(false);

        var pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: false,
            CancellationToken.None).AsTask();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pickTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual("Error connecting to subchannel.", ex.Status.Detail);
        Assert.AreEqual("Test error!", ex.Status.DebugException?.Message);
    }

    [Test]
    public async Task PickAsync_RetryWithDrop_ThrowsError()
    {
        // Arrange
        string? authority = null;
        var testMessageHandler = TestHttpMessageHandler.Create(async request =>
        {
            authority = request.RequestUri!.Authority;
            var reply = new HelloReply { Message = "Hello world" };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var services = new ServiceCollection();
        services.AddNUnitLogger();
        services.AddSingleton<TestResolver>();
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        DropLoadBalancer? loadBalancer = null;
        services.AddSingleton<LoadBalancerFactory>(new DropLoadBalancerFactory(c =>
        {
            loadBalancer = new DropLoadBalancer(c);
            return loadBalancer;
        }));
        services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

        var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
            o.ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        Names = { MethodName.Default },
                        RetryPolicy = new RetryPolicy
                        {
                            MaxAttempts = 5,
                            InitialBackoff = TimeSpan.FromMinutes(10),
                            MaxBackoff = TimeSpan.FromMinutes(10),
                            BackoffMultiplier = 1,
                            RetryableStatusCodes = { StatusCode.DataLoss }
                        }
                    }
                },
                LoadBalancingConfigs = { new LoadBalancingConfig("drop") }
            };
        });

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions().WithWaitForReady(), new HelloRequest());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DataLoss, ex.StatusCode);

        Assert.AreEqual(1, loadBalancer!.PickCount);
    }

    [Test]
    public async Task PickAsync_HedgingWithDrop_ThrowsError()
    {
        // Arrange
        string? authority = null;
        var testMessageHandler = TestHttpMessageHandler.Create(async request =>
        {
            authority = request.RequestUri!.Authority;
            var reply = new HelloReply { Message = "Hello world" };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });

        var services = new ServiceCollection();
        services.AddNUnitLogger();
        services.AddSingleton<TestResolver>();
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        DropLoadBalancer? loadBalancer = null;
        services.AddSingleton<LoadBalancerFactory>(new DropLoadBalancerFactory(c =>
        {
            loadBalancer = new DropLoadBalancer(c);
            return loadBalancer;
        }));
        services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

        var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
            o.ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig
                    {
                        Names = { MethodName.Default },
                        HedgingPolicy = new HedgingPolicy
                        {
                            MaxAttempts = 5,
                            HedgingDelay = TimeSpan.FromMinutes(10),
                            NonFatalStatusCodes = { StatusCode.DataLoss }
                        }
                    }
                },
                LoadBalancingConfigs = { new LoadBalancingConfig("drop") }
            };
        });

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions().WithWaitForReady(), new HelloRequest());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DataLoss, ex.StatusCode);

        Assert.AreEqual(1, loadBalancer!.PickCount);
    }

    [Test]
    public async Task UpdateAddresses_ConnectIsInProgress_InProgressConnectIsCanceledAndRestarted()
    {
        // Arrange
        var services = new ServiceCollection();

        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);
        services.AddLogging(o => o.AddProvider(testProvider));
        services.AddNUnitLogger();

        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var testLogger = loggerFactory.CreateLogger(GetType());

        var resolver = new TestResolver(loggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var connectAddressesChannel = Channel.CreateUnbounded<DnsEndPoint>();

        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        var transportFactory = TestSubchannelTransportFactory.Create(async (s, c) =>
        {
            c.Register(state => ((SyncPoint)state!).Continue(), syncPoint);

            var connectAddress = s.GetAddresses().Single();
            testLogger.LogInformation("Writing connect address " + connectAddress);

            await connectAddressesChannel.Writer.WriteAsync(connectAddress.EndPoint, c);
            await syncPoint.WaitToContinue();

            c.ThrowIfCancellationRequested();
            return new TryConnectResult(ConnectivityState.Ready);
        });
        using var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory);
        clientChannel.ConfigureBalancer(c => new PickFirstBalancer(c, loggerFactory));

        // Act
        _ = clientChannel.ConnectAsync(waitForReady: true, CancellationToken.None).ConfigureAwait(false);

        var connectAddress1 = await connectAddressesChannel.Reader.ReadAsync().AsTask().DefaultTimeout();
        Assert.AreEqual(80, connectAddress1.Port);

        // Endpoints are unchanged so continue connecting...
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });
        Assert.IsFalse(syncPoint.WaitToContinue().IsCompleted);

        // Endpoints change so cancellation + reconnect triggered
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 81)
        });

        await syncPoint.WaitToContinue().DefaultTimeout();

        var connectAddress2 = await connectAddressesChannel.Reader.ReadAsync().AsTask().DefaultTimeout();
        Assert.AreEqual(81, connectAddress2.Port);

        await TestHelpers.AssertIsTrueRetryAsync(() => testSink.Writes.Any(w => w.EventId.Name == "CancelingConnect"), "Wait for CancelingConnect.").DefaultTimeout();
        await TestHelpers.AssertIsTrueRetryAsync(() => testSink.Writes.Any(w => w.EventId.Name == "ConnectCanceled"), "Wait for ConnectCanceled.").DefaultTimeout();
    }

    [Test]
    public async Task PickAsync_DoesNotDeadlockAfterReconnect_WithResolverError()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        await using var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(GetType());

        var resolver = new TestResolver(loggerFactory);

        GrpcChannelOptions channelOptions = new GrpcChannelOptions();
        channelOptions.ServiceConfig = new ServiceConfig()
        {
            LoadBalancingConfigs = { new RoundRobinConfig() }
        };

        var transportFactory = new TestSubchannelTransportFactory();
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory, new[] { new RoundRobinBalancerFactory() });
        // Configure balancer similar to how GrpcChannel constructor does it
        clientChannel.ConfigureBalancer(c => new ChildHandlerLoadBalancer(
            c,
            channelOptions.ServiceConfig,
            clientChannel));

        // Act
        logger.LogInformation("Client connecting.");
        var connectTask = clientChannel.ConnectAsync(waitForReady: true, cancellationToken: CancellationToken.None);

        logger.LogInformation("Starting pick on connecting channel.");
        var pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();

        logger.LogInformation("Waiting for resolve to complete.");
        await resolver.HasResolvedTask.DefaultTimeout();

        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });
        await Task.WhenAll(connectTask, pickTask).DefaultTimeout();

        logger.LogInformation("Simulate transport/network issue.");
        transportFactory.Transports.ForEach(t => t.Disconnect());
        resolver.UpdateError(new Status(StatusCode.Unavailable, "Test error"));

        logger.LogInformation("Starting pick on disconnected channel.");
        pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        // Assert
        // Should not timeout (deadlock)
        logger.LogInformation("Wait for pick task to complete.");
        await pickTask.DefaultTimeout();

        logger.LogInformation("Done.");
    }

    [Test]
    public async Task PickAsync_DoesNotDeadlockAfterReconnect_WithZeroAddressResolved()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        await using var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var resolver = new TestResolver(loggerFactory);

        GrpcChannelOptions channelOptions = new GrpcChannelOptions();
        channelOptions.ServiceConfig = new ServiceConfig()
        {
            LoadBalancingConfigs = { new RoundRobinConfig() }
        };

        var transportFactory = new TestSubchannelTransportFactory();
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory, new[] { new RoundRobinBalancerFactory() });
        // Configure balancer similar to how GrpcChannel constructor does it
        clientChannel.ConfigureBalancer(c => new ChildHandlerLoadBalancer(
            c,
            channelOptions.ServiceConfig,
            clientChannel));

        // Act
        var connectTask = clientChannel.ConnectAsync(waitForReady: true, cancellationToken: CancellationToken.None);
        var pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();

        await resolver.HasResolvedTask.DefaultTimeout();

        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });
        await Task.WhenAll(connectTask, pickTask).DefaultTimeout();

        // Simulate transport/network issue (with resolver reporting no addresses)
        transportFactory.Transports.ForEach(t => t.Disconnect());
        resolver.UpdateAddresses(new List<BalancerAddress>());

        pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        // Assert
        // Should not timeout (deadlock)
        await pickTask.DefaultTimeout();
    }

    [Test]
    public async Task PickAsync_UpdateAddressesWhileRequestingConnection_DoesNotDeadlock()
    {
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);

        services.AddLogging(b =>
        {
            b.AddProvider(testProvider);
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var resolver = new TestResolver(loggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var channelOptions = new GrpcChannelOptions();

        var transportFactory = new TestSubchannelTransportFactory();
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory, new[] { new PickFirstBalancerFactory() });
        // Configure balancer similar to how GrpcChannel constructor does it
        clientChannel.ConfigureBalancer(c => new ChildHandlerLoadBalancer(
            c,
            channelOptions.ServiceConfig,
            clientChannel));

        await clientChannel.ConnectAsync(waitForReady: true, cancellationToken: CancellationToken.None);

        transportFactory.Transports.ForEach(t => t.Disconnect());

        var requestConnectionSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        testSink.MessageLogged += (w) =>
        {
            if (w.EventId.Name == "ConnectionRequested")
            {
                requestConnectionSyncPoint.WaitToContinue().Wait();
            }
        };

        // Task should pause when requesting connection because of the logger sink.
        var pickTask = Task.Run(() => clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask());

        // Wait until we're paused on requesting a connection.
        await requestConnectionSyncPoint.WaitForSyncPoint().DefaultTimeout();

        // Update addresses while requesting a connection.
        var updateAddressesTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateAddressesTask = Task.Run(() =>
        {
            updateAddressesTcs.TrySetResult(null);
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 81)
            });
        });

        // There isn't a clean way to wait for UpdateAddresses to be waiting for the subchannel lock.
        // Use a long delay to ensure we're waiting for the lock and are in the right state.
        await updateAddressesTcs.Task.DefaultTimeout();
        await Task.Delay(1000);
        requestConnectionSyncPoint.Continue();

        // Ensure the pick completes without deadlock.
        try
        {
            await pickTask.DefaultTimeout();
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException("Likely deadlock when picking subchannel.", ex);
        }
    }

    [Test]
    public async Task PickAsync_MultipleRequestsRequestConnect_SingleConnectAttempt()
    {
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);

        services.AddLogging(b =>
        {
            b.AddProvider(testProvider);
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(nameof(PickAsync_MultipleRequestsRequestConnect_SingleConnectAttempt));

        var requestConnectionChannel = Channel.CreateUnbounded<SyncPoint>();
        var requestConnectionSyncPoint1 = new SyncPoint(runContinuationsAsynchronously: true);
        var requestConnectionSyncPoint2 = new SyncPoint(runContinuationsAsynchronously: true);
        requestConnectionChannel.Writer.TryWrite(requestConnectionSyncPoint1);
        requestConnectionChannel.Writer.TryWrite(requestConnectionSyncPoint2);

        var connectingSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        var resolver = new TestResolver(loggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var channelOptions = new GrpcChannelOptions();
        var acting = false;
        var transportFactory = TestSubchannelTransportFactory.Create(async (subChannel, attempt, cancellationToken) =>
        {
            cancellationToken.Register(() =>
            {
                logger.LogError("Connect cancellation token canceled.");
            });

            if (!acting)
            {
                return new TryConnectResult(ConnectivityState.Ready);
            }

            await connectingSyncPoint.WaitToContinue().WaitAsync(cancellationToken);

            Assert.IsFalse(cancellationToken.IsCancellationRequested, "Cancellation token should not be canceled.");

            return new TryConnectResult(ConnectivityState.Ready);
        });
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory, new[] { new PickFirstBalancerFactory() });
        // Configure balancer similar to how GrpcChannel constructor does it
        clientChannel.ConfigureBalancer(c => new ChildHandlerLoadBalancer(
            c,
            channelOptions.ServiceConfig,
            clientChannel));

        await clientChannel.ConnectAsync(waitForReady: true, cancellationToken: CancellationToken.None);

        transportFactory.Transports.ForEach(t => t.Disconnect());

        testSink.MessageLogged += (w) =>
        {
            if (w.EventId.Name == "StartingConnectionRequest")
            {
                if (!requestConnectionChannel.Reader.TryRead(out var syncPoint))
                {
                    throw new InvalidOperationException("Channel should have sync point.");
                }
                syncPoint.WaitToContinue().Wait();
            }
        };

        acting = true;

        logger.LogInformation("Start first pick.");
        var pickTask1 = Task.Run(() => clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask());

        logger.LogInformation("Wait for first pick to request connection.");
        await requestConnectionSyncPoint1.WaitForSyncPoint().DefaultTimeout();

        logger.LogInformation("Start second pick.");
        var pickTask2 = Task.Run(() => clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask());

        logger.LogInformation("Wait for second pick to request connection.");
        await requestConnectionSyncPoint2.WaitForSyncPoint().DefaultTimeout();

        logger.LogInformation("Allow first pick to start connecting.");
        requestConnectionSyncPoint1.Continue();
        await connectingSyncPoint.WaitForSyncPoint();

        var connectionRequestedInNonIdleStateTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        testSink.MessageLogged += (w) =>
        {
            if (w.EventId.Name == "ConnectionRequestedInNonIdleState")
            {
                connectionRequestedInNonIdleStateTcs.TrySetResult(null);
            }
        };

        logger.LogInformation("Allow second pick to wait for connecting to complete.");
        requestConnectionSyncPoint2.Continue();

        logger.LogInformation("Wait for second pick to report that there is already a connection requested.");
        await connectionRequestedInNonIdleStateTcs.Task.DefaultTimeout();

        logger.LogInformation("Allow first pick connecting to complete.");
        connectingSyncPoint.Continue();

        logger.LogInformation("Wait for both picks to complete successfully.");
        await pickTask1.DefaultTimeout();
        await pickTask2.DefaultTimeout();
    }

    [Test]
    public async Task PickAsync_ExecutionContext_DoesNotCaptureAsyncLocalsInConnect()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        await using var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var resolver = new TestResolver(loggerFactory);

        GrpcChannelOptions channelOptions = new GrpcChannelOptions();
        channelOptions.ServiceConfig = new ServiceConfig()
        {
            LoadBalancingConfigs = { new RoundRobinConfig() }
        };

        AsyncLocal<object> asyncLocal = new AsyncLocal<object>();
        asyncLocal.Value = new object();

        var callbackAsyncLocalValues = new List<object>();

        var transportFactory = TestSubchannelTransportFactory.Create((subchannel, cancellationToken) =>
        {
            callbackAsyncLocalValues.Add(asyncLocal.Value);
            if (callbackAsyncLocalValues.Count >= 2)
            {
                return Task.FromResult(new TryConnectResult(ConnectivityState.Ready, ConnectResult.Success));
            }

            return Task.FromResult(new TryConnectResult(ConnectivityState.TransientFailure, ConnectResult.Failure));
        });
        var backoffPolicy = new TestBackoffPolicyFactory(TimeSpan.FromMilliseconds(200));
        var clientChannel = CreateConnectionManager(loggerFactory, resolver, transportFactory, new[] { new RoundRobinBalancerFactory() }, backoffPolicy);
        // Configure balancer similar to how GrpcChannel constructor does it
        clientChannel.ConfigureBalancer(c => new ChildHandlerLoadBalancer(
            c,
            channelOptions.ServiceConfig,
            clientChannel));

        // Act
        var connectTask = clientChannel.ConnectAsync(waitForReady: true, cancellationToken: CancellationToken.None);
        var pickTask = clientChannel.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: true,
            CancellationToken.None).AsTask();

        await resolver.HasResolvedTask.DefaultTimeout();

        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });
        await Task.WhenAll(connectTask, pickTask).DefaultTimeout();

        // Assert
        Assert.AreEqual(2, callbackAsyncLocalValues.Count);
        Assert.IsNull(callbackAsyncLocalValues[0]);
        Assert.IsNull(callbackAsyncLocalValues[1]);
    }

    private static ConnectionManager CreateConnectionManager(
        ILoggerFactory loggerFactory,
        Resolver resolver,
        TestSubchannelTransportFactory transportFactory,
        LoadBalancerFactory[]? loadBalancerFactories = null,
        IBackoffPolicyFactory? backoffPolicyFactory = null)
    {
        return new ConnectionManager(
            resolver,
            disableResolverServiceConfig: false,
            loggerFactory,
            backoffPolicyFactory ?? new TestBackoffPolicyFactory(),
            transportFactory,
            loadBalancerFactories ?? Array.Empty<LoadBalancerFactory>());
    }

    private class DropLoadBalancer : LoadBalancer
    {
        private readonly IChannelControlHelper _controller;

        public DropLoadBalancer(IChannelControlHelper controller)
        {
            _controller = controller;
        }

        public int PickCount { get; private set; }

        public override void RequestConnection()
        {
        }

        public override void UpdateChannelState(ChannelState state)
        {
            _controller.UpdateState(new BalancerState(ConnectivityState.TransientFailure, new DropSubchannelPicker(this)));
        }

        private class DropSubchannelPicker : SubchannelPicker
        {
            private readonly DropLoadBalancer _loadBalancer;

            public DropSubchannelPicker(DropLoadBalancer loadBalancer)
            {
                _loadBalancer = loadBalancer;
            }

            public override PickResult Pick(PickContext context)
            {
                _loadBalancer.PickCount++;
                return PickResult.ForDrop(new Status(StatusCode.DataLoss, string.Empty));
            }
        }
    }

    private class DropLoadBalancerFactory : LoadBalancerFactory
    {
        private readonly Func<IChannelControlHelper, DropLoadBalancer> _loadBalancerFunc;

        public DropLoadBalancerFactory(Func<IChannelControlHelper, DropLoadBalancer> loadBalancerFunc)
        {
            _loadBalancerFunc = loadBalancerFunc;
        }

        public override string Name => "drop";

        public override LoadBalancer Create(LoadBalancerOptions options)
        {
            return _loadBalancerFunc(options.Controller);
        }
    }
}
#endif
