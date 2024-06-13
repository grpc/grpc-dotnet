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
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer;

[TestFixture]
public class PickFirstBalancerTests
{
    [Test]
    public async Task ChangeAddresses_HasReadySubchannel_OldSubchannelShutdown()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));

        var subChannelConnections = new List<Subchannel>();
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            lock (subChannelConnections)
            {
                subChannelConnections.Add(s);
            }
            return Task.FromResult(new TryConnectResult(ConnectivityState.Ready));
        });
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerProvider>().CreateLogger(GetType().FullName!);

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        var stateChangedTask = BalancerWaitHelpers.WaitForChannelStateAsync(logger, channel, ConnectivityState.Ready);
        await channel.ConnectAsync();

        // Assert
        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        await stateChangedTask.DefaultTimeout();

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 81) });

        stateChangedTask = BalancerWaitHelpers.WaitForChannelStateAsync(logger, channel, ConnectivityState.Ready);

        var newSubchannels = channel.ConnectionManager.GetSubchannels();
        CollectionAssert.AreEqual(subchannels, newSubchannels);

        await stateChangedTask.DefaultTimeout();

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 81), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        lock (subChannelConnections)
        {
            Assert.AreEqual(2, subChannelConnections.Count);
            Assert.AreSame(subChannelConnections[0], subChannelConnections[1]);
        }
    }

    [Test]
    public async Task ResolverError_HasReadySubchannel_SubchannelUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        var transportFactory = new TestSubchannelTransportFactory();
        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        await channel.ConnectAsync().DefaultTimeout();

        // Assert
        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);

        // Wait for TryConnect to be called so state is connected
        await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        resolver.UpdateError(new Status(StatusCode.Internal, "Error!", new Exception("Test exception!")));
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        // Existing channel continutes to run
        var newSubchannels = channel.ConnectionManager.GetSubchannels();
        CollectionAssert.AreEqual(subchannels, newSubchannels);
        Assert.AreEqual(1, newSubchannels.Count);

        await channel.ConnectionManager.PickAsync(new PickContext { Request = new HttpRequestMessage() }, waitForReady: false, CancellationToken.None).AsTask().DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, newSubchannels[0].State);
    }

    [Test]
    public async Task ResolverError_HasFailedSubchannel_SubchannelShutdown()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var transportFactory = TestSubchannelTransportFactory.Create((s, c) => Task.FromResult(new TryConnectResult(ConnectivityState.TransientFailure)));
        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerProvider>().CreateLogger(GetType().FullName!);

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        var stateChangedTask = BalancerWaitHelpers.WaitForChannelStateAsync(logger, channel, ConnectivityState.TransientFailure);
        _ = channel.ConnectAsync();

        // Assert
        await resolver.HasResolvedTask.DefaultTimeout();

        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        await stateChangedTask.DefaultTimeout();

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(ConnectivityState.TransientFailure, subchannels[0].State);

        resolver.UpdateError(new Status(StatusCode.Internal, "Error!", new Exception("Test exception!")));
        Assert.AreEqual(ConnectivityState.Shutdown, subchannels[0].State);

        var pickContext = new PickContext { Request = new HttpRequestMessage() };
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await channel.ConnectionManager.PickAsync(pickContext, waitForReady: false, CancellationToken.None)).DefaultTimeout();
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual("Error!", ex.Status.Detail);
        Assert.AreEqual("Test exception!", ex.Status.DebugException!.Message);
    }

    [Test]
    public async Task RequestConnection_InitialConnectionFails_ExponentialBackoff()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        SyncPoint syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var connectivityState = ConnectivityState.TransientFailure;

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(TestSubchannelTransportFactory.Create(async (s, c) =>
        {
            await syncPoint.WaitToContinue();
            return new TryConnectResult(connectivityState);
        }));

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        var connectTask = channel.ConnectAsync();

        // First connection fails
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        syncPoint.Continue();
        syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        // TODO
        //Assert.IsFalse(connectTask.IsCompleted);

        // Second connection succeeds
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        connectivityState = ConnectivityState.Ready;
        syncPoint.Continue();

        await connectTask.DefaultTimeout();

        // Assert
        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);
    }

    [Test]
    public async Task RequestConnection_InitialConnectionEnds_EntersIdleState()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        var transportConnectCount = 0;
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            transportConnectCount++;
            return Task.FromResult(new TryConnectResult(ConnectivityState.Ready));
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        await channel.ConnectAsync();

        // Assert
        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        var stateChangedTask = channel.WaitForStateChangedAsync(ConnectivityState.Ready);

        transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);

        await stateChangedTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Idle, channel.State);

        Assert.AreEqual(1, transportConnectCount);
    }

    [Test]
    public async Task RequestConnection_IdleConnectionConnectAsync_StateToReady()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        var transportConnectCount = 0;
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            transportConnectCount++;
            return Task.FromResult(new TryConnectResult(ConnectivityState.Ready));
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        await channel.ConnectAsync().DefaultTimeout();

        transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);

        Assert.AreEqual(ConnectivityState.Idle, channel.State);

        var stateChangedTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);

        await channel.ConnectAsync().DefaultTimeout();

        await stateChangedTask.DefaultTimeout();

        Assert.AreEqual(2, transportConnectCount);
    }

    [Test]
    public async Task RequestConnection_IdleConnectionPick_StateToReady()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        var transportConnectCount = 0;
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            transportConnectCount++;
            return Task.FromResult(new TryConnectResult(ConnectivityState.Ready));
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILoggerProvider>().CreateLogger(GetType().FullName!);

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        await channel.ConnectAsync();

        transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);
        Assert.AreEqual(ConnectivityState.Idle, channel.State);

        logger.LogInformation("Waiting for state change from current state of idle.");
        var stateChangedTask = BalancerWaitHelpers.WaitForChannelStateAsync(logger, channel, ConnectivityState.Ready);

        logger.LogInformation("Starting PickAsync");
        var pick = await channel.ConnectionManager.PickAsync(
            new PickContext { Request = new HttpRequestMessage() },
            waitForReady: false,
            CancellationToken.None).AsTask().DefaultTimeout();

        logger.LogInformation("Waiting for state change to complete");
        await stateChangedTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, channel.State);
        Assert.AreEqual(2, transportConnectCount);
        Assert.AreEqual("localhost", pick.Address.EndPoint.Host);
        Assert.AreEqual(80, pick.Address.EndPoint.Port);
    }

    [Test]
    public async Task UnaryCall_TransportConnecting_OnePickStarted()
    {
        // Arrange
        var services = new ServiceCollection();

        var testSink = new TestSink();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        ILogger logger = null!;

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transportConnectCount = 0;
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            transportConnectCount++;

            logger.LogInformation("Connect count: " + transportConnectCount);
            if (transportConnectCount == 2)
            {
                tcs.SetResult(null);
            }

            return Task.FromResult(new TryConnectResult(ConnectivityState.Connecting));
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var serviceProvider = services.BuildServiceProvider();
        logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var invoker = HttpClientCallInvokerFactory.Create(handler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
        });

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions().WithWaitForReady(), new HelloRequest());
        await tcs.Task.DefaultTimeout();

        // Assert
        var pickStartedCount = testSink.Writes.Count(w => w.EventId.Name == "PickStarted");
        Assert.AreEqual(1, pickStartedCount);
    }

    [Test]
    public async Task UnaryCall_TransportConnecting_ErrorAfterTransientFailure()
    {
        // Arrange
        var services = new ServiceCollection();

        var testSink = new TestSink();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        ILogger logger = null!;

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transportConnectCount = 0;
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            transportConnectCount++;

            logger.LogInformation("Connect count: " + transportConnectCount);
            if (transportConnectCount == 2)
            {
                tcs.SetResult(null);
            }

            return Task.FromResult(new TryConnectResult(ConnectivityState.Connecting));
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var serviceProvider = services.BuildServiceProvider();
        logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType());

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var invoker = HttpClientCallInvokerFactory.Create(handler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
        });

        _ = invoker.Channel.ConnectAsync();

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        await tcs.Task.DefaultTimeout();

        // Assert
        await TestHelpers.AssertIsTrueRetryAsync(
            () =>
            {
                var pickStartedCount = testSink.Writes.Count(w => w.EventId.Name == "PickStarted");
                return pickStartedCount >= 1;
            },
            "Wait for pick started.").DefaultTimeout();

        transportFactory.Transports.Single().UpdateState(ConnectivityState.TransientFailure, new Status(StatusCode.Unavailable, "An error"));

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual("An error", ex.Status.Detail);
    }

    [Test]
    public async Task DeadlineExceeded_MultipleCalls_CallsWaitForDeadline()
    {
        // Arrange
        var services = new ServiceCollection();

        var testSink = new TestSink();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            return Task.FromResult(new TryConnectResult(ConnectivityState.Connecting));
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var serviceProvider = services.BuildServiceProvider();

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var invoker = HttpClientCallInvokerFactory.Create(handler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
        });

        // Act 1
        var call1 = invoker.AsyncUnaryCall(
            ClientTestHelpers.ServiceMethod,
            string.Empty,
            new CallOptions(deadline: DateTime.UtcNow.Add(TimeSpan.FromSeconds(0.1))),
            new HelloRequest());

        // Assert 1
        var ex1 = await ExceptionAssert.ThrowsAsync<RpcException>(() => call1.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex1.StatusCode);

        // Act 2
        var call2 = invoker.AsyncUnaryCall(
            ClientTestHelpers.ServiceMethod,
            string.Empty,
            new CallOptions(deadline: DateTime.UtcNow.Add(TimeSpan.FromSeconds(0.1))),
            new HelloRequest());

        // Assert 2
        var ex2 = await ExceptionAssert.ThrowsAsync<RpcException>(() => call2.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex2.StatusCode);
    }

    [Test]
    public async Task ConnectTimeout_MultipleCalls_AttemptReconnect()
    {
        // Arrange
        var services = new ServiceCollection();

        var testSink = new TestSink();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();

        var resolver = new TestResolver();
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("localhost", 80) });

        var tryConnectTcs = new TaskCompletionSource<TryConnectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transportFactory = TestSubchannelTransportFactory.Create((s, ct) =>
        {
            return tryConnectTcs.Task;
        });

        services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

        var serviceProvider = services.BuildServiceProvider();

        var logger = serviceProvider.GetRequiredService<ILogger<PickFirstBalancerTests>>();
        var handler = ClientTestHelpers.CreateTestMessageHandler(new HelloReply { Message = "Message!" });
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var invoker = HttpClientCallInvokerFactory.Create(handler, "test:///localhost", configure: o =>
        {
            o.Credentials = ChannelCredentials.Insecure;
            o.ServiceProvider = services.BuildServiceProvider();
        });

        // Act 1
        logger.LogInformation("Client making call 1.");
        var call1 = invoker.AsyncUnaryCall(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
        tryConnectTcs.SetResult(new TryConnectResult(ConnectivityState.TransientFailure, ConnectResult.Timeout));

        // Assert 1
        logger.LogInformation("Client waiting for call 1.");
        var ex1 = await ExceptionAssert.ThrowsAsync<RpcException>(() => call1.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Internal, ex1.StatusCode);

        logger.LogInformation("Client waiting for state change.");
        await invoker.Channel.WaitForStateChangedAsync(ConnectivityState.TransientFailure).DefaultTimeout();

        // Act 2
        tryConnectTcs = new TaskCompletionSource<TryConnectResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        tryConnectTcs.SetResult(new TryConnectResult(ConnectivityState.Ready));
        logger.LogInformation("Client making call 2.");
        var call2 = invoker.AsyncUnaryCall(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

        // Assert 2
        logger.LogInformation("Client waiting for call 2.");
        var response = await call2.ResponseAsync.DefaultTimeout();
        Assert.AreEqual("Message!", response.Message);
    }
}
#endif
