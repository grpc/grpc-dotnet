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

using Greet;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
#if SUPPORT_LOAD_BALANCING
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
#endif

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GrpcChannelTests
    {
        [Test]
        public void Build_AddressWithoutHost_Error()
        {
#if SUPPORT_LOAD_BALANCING
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test.example.com:5001"))!;

            // Assert
            Assert.AreEqual("No address resolver configured for the scheme 'test.example.com'.", ex.Message);
#else
            // Arrange & Act
            var ex = Assert.Throws<ArgumentException>(() => GrpcChannel.ForAddress("test.example.com:5001"))!;

            // Assert
            Assert.AreEqual("Address 'test.example.com:5001' doesn't have a host. Address should include a scheme, host, and optional port. For example, 'https://localhost:5001'.", ex.Message);
#endif
        }

        [TestCase("https://localhost:5001/path", true)]
        [TestCase("https://localhost:5001/?query=ya", true)]
        [TestCase("https://localhost:5001//", true)]
        [TestCase("https://localhost:5001/", false)]
        [TestCase("https://localhost:5001", false)]
        public void Build_AddressWithPath_Log(string address, bool hasPathOrQuery)
        {
            // Arrange
            var testSink = new TestSink();
            var testFactory = new TestLoggerFactory(testSink, enabled: true);

            // Act
            GrpcChannel.ForAddress(address, CreateGrpcChannelOptions(o => o.LoggerFactory = testFactory));

            // Assert
            var log = testSink.Writes.SingleOrDefault(w => w.EventId.Name == "AddressPathUnused");
            if (hasPathOrQuery)
            {
                Assert.IsNotNull(log);
                Assert.AreEqual(LogLevel.Debug, log!.LogLevel);

                var message = $"The path in the channel's address '{address}' won't be used when making gRPC calls. " +
                    "A DelegatingHandler can be used to include a path with gRPC calls. See https://aka.ms/aspnet/grpc/subdir for details.";
                Assert.AreEqual(message, log.Message);
            }
            else
            {
                Assert.IsNull(log);
            }
        }

        [Test]
        public void Build_SslCredentialsWithHttps_Success()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("https://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials()));

            // Assert
            Assert.IsTrue(channel.IsSecure);
        }

        [TestCase("http://localhost")]
        [TestCase("HTTP://localhost")]
        public void Build_SslCredentialsWithHttp_ThrowsError(string address)
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress(address,
                CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials())))!;

            // Assert
            Assert.AreEqual("Channel is configured with secure channel credentials and can't use a HttpClient with a 'http' scheme.", ex.Message);
        }

        [TestCase("https://localhost")]
        [TestCase("HTTPS://localhost")]
        public void Build_SslCredentialsWithArgs_ThrowsError(string address)
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress(address,
                CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials("rootCertificates!!!"))))!;

            // Assert
            Assert.AreEqual(
                "SslCredentials with non-null arguments is not supported by GrpcChannel. " +
                "GrpcChannel uses HttpClient to make gRPC calls and HttpClient automatically loads root certificates from the operating system certificate store. " +
                "Client certificates should be configured on HttpClient. See https://aka.ms/aspnet/grpc/certauth for details.", ex.Message);
        }

        [Test]
        public void Build_InsecureCredentialsWithHttp_Success()
        {
            // Arrange & Act
            var channel = GrpcChannel.ForAddress("http://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = ChannelCredentials.Insecure));

            // Assert
            Assert.IsFalse(channel.IsSecure);
        }

        private static GrpcChannelOptions CreateGrpcChannelOptions(Action<GrpcChannelOptions>? func = null)
        {
            var o = new GrpcChannelOptions();
#if NET472
            // An error is thrown if no handler is specified by .NET Standard 2.0 target.
            o.HttpHandler = new NullHttpHandler();
#endif
            func?.Invoke(o);
            return o;
        }

        [Test]
        public void Build_InsecureCredentialsWithHttps_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost",
                CreateGrpcChannelOptions(o => o.Credentials = ChannelCredentials.Insecure)))!;

            // Assert
            Assert.AreEqual("Channel is configured with insecure channel credentials and can't use a HttpClient with a 'https' scheme.", ex.Message);
        }

        [Test]
        public void Build_HttpClientAndHttpHandler_ThrowsError()
        {
            // Arrange & Act
            var ex = Assert.Throws<ArgumentException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = new HttpClient(),
                HttpHandler = new HttpClientHandler()
            }))!;

            // Assert
            Assert.AreEqual("HttpClient and HttpHandler have been configured. Only one HTTP caller can be specified.", ex.Message);
        }

        [Test]
        public async Task Build_HttpClient_UsedForRequestsAsync()
        {
            // Arrange
            var handler = new ExceptionHttpMessageHandler("HttpClient");
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = new HttpClient(handler)
            });
            var client = new Greeter.GreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await client.SayHelloAsync(new HelloRequest()));

            // Assert
            Assert.AreEqual("HttpClient", ex.Status.DebugException!.Message);
        }

        [Test]
        public async Task Build_HttpHandler_UsedForRequestsAsync()
        {
            // Arrange
            var handler = new ExceptionHttpMessageHandler("HttpHandler");
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler
            });
            var client = new Greeter.GreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await client.SayHelloAsync(new HelloRequest())).DefaultTimeout();

            // Assert
            Assert.AreEqual("HttpHandler", ex.Status.DebugException!.Message);
        }

#if NET472
        [Test]
        public void Build_NoHttpProviderOnNetFx_Throw()
        {
            // Arrange & Act
            var ex = Assert.Throws<PlatformNotSupportedException>(() => GrpcChannel.ForAddress("https://localhost"))!;

            // Assert
            var message =
                $"gRPC requires extra configuration on .NET implementations that don't support gRPC over HTTP/2. " +
                $"An HTTP provider must be specified using {nameof(GrpcChannelOptions)}.{nameof(GrpcChannelOptions.HttpHandler)}." +
                $"The configured HTTP provider must either support HTTP/2 or be configured to use gRPC-Web. " +
                $"See https://aka.ms/aspnet/grpc/netstandard for details.";

            Assert.AreEqual(message, ex.Message);
        }
#endif

        [Test]
        public void Build_ServiceConfigDuplicateMethodConfigNames_Error()
        {
            // Arrange
            var options = CreateGrpcChannelOptions(o => o.ServiceConfig = new ServiceConfig
            {
                MethodConfigs =
                {
                    new MethodConfig { Names = { MethodName.Default } },
                    new MethodConfig { Names = { MethodName.Default } }
                }
            });

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", options))!;

            // Assert
            Assert.AreEqual("Duplicate method config found. Service: '', method: ''.", ex.Message);
        }

        [Test]
        public void Dispose_NotCalled_NotDisposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

            // Act (nothing)

            // Assert
            Assert.IsFalse(channel.Disposed);
        }

#if !NET472
        [Test]
        public void Dispose_Called_Disposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost");

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.Throws<ObjectDisposedException>(() => channel.HttpInvoker.SendAsync(new HttpRequestMessage(), CancellationToken.None));
        }
#endif

        [Test]
        public void Dispose_CalledMultipleTimes_Disposed()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

            // Act
            channel.Dispose();
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
        }

        [Test]
        public void Dispose_CreateCallInvoker_ThrowError()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

            // Act
            channel.Dispose();

            // Assert
            Assert.Throws<ObjectDisposedException>(() => channel.CreateCallInvoker());
        }

        [Test]
        public async Task Dispose_StartCallOnClient_ThrowError()
        {
            // Arrange
            var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());
            var client = new Greet.Greeter.GreeterClient(channel);

            // Act
            channel.Dispose();

            // Assert
            await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => client.SayHelloAsync(new Greet.HelloRequest()).ResponseAsync);
        }

        [Test]
        public void Dispose_CalledWhenHttpClientSpecified_HttpClientNotDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.IsFalse(handler.Disposed);
        }

        [Test]
        public void Dispose_CalledWhenHttpClientSpecifiedAndHttpClientDisposedTrue_HttpClientDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var httpClient = new HttpClient(handler);
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpClient = httpClient,
                DisposeHttpClient = true
            });

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.IsTrue(handler.Disposed);
        }

        [Test]
        public void Dispose_CalledWhenHttpMessageHandlerSpecifiedAndHttpClientDisposedTrue_HttpClientDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler,
                DisposeHttpClient = true
            });

            // Act
            channel.Dispose();

            // Assert
            Assert.IsTrue(channel.Disposed);
            Assert.IsTrue(handler.Disposed);
        }

        [Test]
        public async Task Dispose_CalledWhileActiveCalls_ActiveCallsDisposed()
        {
            // Arrange
            var handler = new TestHttpMessageHandler();
            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler
            });

            var client = new Greeter.GreeterClient(channel);
            var call = client.SayHelloAsync(new HelloRequest());

            var exTask = ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
            Assert.IsFalse(exTask.IsCompleted);
            Assert.AreEqual(1, channel.ActiveCalls.Count);

            // Act
            channel.Dispose();

            // Assert
            var ex = await exTask.DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);

            Assert.IsTrue(channel.Disposed);
            Assert.AreEqual(0, channel.ActiveCalls.Count);
        }

#if SUPPORT_LOAD_BALANCING
        [Test]
        public void Resolver_SocketHttpHandlerWithConnectCallback_Error()
        {
            ConfigureLoadBalancingWithInvalidHttpHandler(o =>
            {
                o.HttpHandler = new SocketsHttpHandler
                {
                    ConnectCallback = (context, ct) => new ValueTask<Stream>(new MemoryStream())
                };
            });
        }

        [Test]
        public void Resolver_HttpClientHandler_Error()
        {
            ConfigureLoadBalancingWithInvalidHttpHandler(o =>
            {
                o.HttpHandler = new HttpClientHandler();
            });
        }

        [Test]
        public void Resolver_HttpClient_Error()
        {
            ConfigureLoadBalancingWithInvalidHttpHandler(o =>
            {
                o.HttpClient = new HttpClient();
            });
        }

        private static void ConfigureLoadBalancingWithInvalidHttpHandler(Action<GrpcChannelOptions> channelOptionsFunc)
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();

            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                Credentials = ChannelCredentials.Insecure
            };
            channelOptionsFunc(channelOptions);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost", channelOptions))!;

            // Assert
            Assert.AreEqual("Channel is configured with an HTTP transport doesn't support client-side load balancing or connectivity state tracking. " +
                "The underlying HTTP transport must be a SocketsHttpHandler with no SocketsHttpHandler.ConnectCallback configured. " +
                "The HTTP transport must be configured on the channel using GrpcChannelOptions.HttpHandler.", ex.Message);
        }

        [Test]
        public void Resolver_NoChannelCredentials_Error()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost", channelOptions))!;

            // Assert
            Assert.AreEqual("Unable to determine the TLS configuration of the channel from address 'test:///localhost'. " +
                "GrpcChannelOptions.Credentials must be specified when the address doesn't have a 'http' or 'https' scheme. " +
                "To call TLS endpoints, set credentials to 'new SslCredentials()'. " +
                "To call non-TLS endpoints, set credentials to 'ChannelCredentials.Insecure'.", ex.Message);
        }

        [Test]
        public async Task State_ConnectAndDispose_StateChanges()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

            // Assert
            Assert.AreEqual(ConnectivityState.Idle, channel.State);

            await channel.ConnectAsync().DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, channel.State);

            channel.Dispose();
            Assert.AreEqual(ConnectivityState.Shutdown, channel.State);
        }

        [Test]
        public async Task WaitForStateChangedAsync_ConnectAndDispose_StateChanges()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

            var waitForNonIdleTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);

            // Assert
            Assert.AreEqual(ConnectivityState.Idle, channel.State);
            Assert.IsFalse(waitForNonIdleTask.IsCompleted);

            await channel.ConnectAsync().DefaultTimeout();
            await waitForNonIdleTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, channel.State);

            var waitForNonReadyTask = channel.WaitForStateChangedAsync(ConnectivityState.Ready);
            Assert.IsFalse(waitForNonReadyTask.IsCompleted);

            channel.Dispose();
            await waitForNonReadyTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Shutdown, channel.State);

            waitForNonReadyTask = channel.WaitForStateChangedAsync(ConnectivityState.Ready);
            Assert.IsTrue(waitForNonReadyTask.IsCompleted);
        }

        [Test]
        public async Task WaitForStateChangedAsync_CancellationTokenBeforeEvent_StateChanges()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };
            var cts = new CancellationTokenSource();

            // Act
            var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

            var waitForNonIdleTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);
            var waitForNonIdleWithCancellationTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);
            var waitForNonIdleWithCancellationDupeTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);

            // Assert
            Assert.IsFalse(waitForNonIdleTask.IsCompleted);
            Assert.IsFalse(waitForNonIdleWithCancellationTask.IsCompleted);
            Assert.IsFalse(waitForNonIdleWithCancellationDupeTask.IsCompleted);

            cts.Cancel();
            await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => waitForNonIdleWithCancellationTask).DefaultTimeout();
            await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => waitForNonIdleWithCancellationDupeTask).DefaultTimeout();

            await channel.ConnectAsync().DefaultTimeout();
            await waitForNonIdleTask.DefaultTimeout();
        }

        [Test]
        public async Task WaitForStateChangedAsync_CancellationTokenAfterEvent_StateChanges()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };
            var cts = new CancellationTokenSource();

            // Act
            var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

            var waitForNonIdleTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);
            var waitForNonIdleWithCancellationTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);
            var waitForNonIdleWithCancellationDupeTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);

            // Assert
            Assert.IsFalse(waitForNonIdleTask.IsCompleted);
            Assert.IsFalse(waitForNonIdleWithCancellationTask.IsCompleted);
            Assert.IsFalse(waitForNonIdleWithCancellationDupeTask.IsCompleted);

            await channel.ConnectAsync().DefaultTimeout();
            await waitForNonIdleTask.DefaultTimeout();
            await waitForNonIdleWithCancellationTask.DefaultTimeout();
            await waitForNonIdleWithCancellationDupeTask.DefaultTimeout();

            cts.Cancel();
        }

        [Test]
        public async Task ConnectAsync_ShiftThroughStates_CompleteOnReady()
        {
            // Arrange
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            var currentConnectivityState = ConnectivityState.TransientFailure;

            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory(async (s, c) =>
            {
                await syncPoint.WaitToContinue();
                return currentConnectivityState;
            }));

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

            var waitForStateTask = WaitForStateAsync(channel, ConnectivityState.Connecting);

            // Assert
            Assert.IsFalse(waitForStateTask.IsCompleted);

            var connectTask = channel.ConnectAsync();
            Assert.IsFalse(connectTask.IsCompleted);

            await waitForStateTask.DefaultTimeout();

            waitForStateTask = WaitForStateAsync(channel, ConnectivityState.TransientFailure);

            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            syncPoint.Continue();
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            await waitForStateTask.DefaultTimeout();

            waitForStateTask = WaitForStateAsync(channel, ConnectivityState.Ready);
            Assert.IsFalse(connectTask.IsCompleted);

            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            currentConnectivityState = ConnectivityState.Ready;
            syncPoint.Continue();
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            await connectTask.DefaultTimeout();
            await waitForStateTask.DefaultTimeout();

            Assert.AreEqual(ConnectivityState.Ready, channel.State);

            waitForStateTask = WaitForStateAsync(channel, ConnectivityState.Ready);
            channel.Dispose();

            await waitForStateTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Shutdown, channel.State);
        }

        private static async Task WaitForStateAsync(GrpcChannel channel, ConnectivityState state)
        {
            while (true)
            {
                var currentState = channel.State;
                
                if (currentState == state)
                {
                    return;
                }

                await channel.WaitForStateChangedAsync(currentState);
            }
        }

        [Test]
        public async Task ConnectAsync_ConnectivityNotSupported_Error()
        {
            await ConnectivityActionOnChannelWhenConnectivityNotSupported(channel => Task.FromResult(channel.ConnectAsync()));
        }

        [Test]
        public async Task State_ConnectivityNotSupported_Error()
        {
            await ConnectivityActionOnChannelWhenConnectivityNotSupported(channel =>
            {
                Console.WriteLine(channel.State);
                return Task.CompletedTask;
            });
        }

        [Test]
        public async Task WaitForStateChangedAsync_ConnectivityNotSupported_Error()
        {
            await ConnectivityActionOnChannelWhenConnectivityNotSupported(channel => channel.WaitForStateChangedAsync(channel.State));
        }

        private static async Task ConnectivityActionOnChannelWhenConnectivityNotSupported(Func<GrpcChannel, Task> action)
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();

            var channelOptions = new GrpcChannelOptions
            {
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = new TestHttpMessageHandler()
            };

            // Act
            var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => action(channel)).DefaultTimeout();

            // Assert
            Assert.AreEqual("Channel is configured with an HTTP transport doesn't support client-side load balancing or connectivity state tracking. " +
                "The underlying HTTP transport must be a SocketsHttpHandler with no SocketsHttpHandler.ConnectCallback configured. " +
                "The HTTP transport must be configured on the channel using GrpcChannelOptions.HttpHandler.", ex.Message);
        }

        [Test]
        public void Resolver_MatchInServiceProvider_Success()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);

            // Assert
            Assert.IsInstanceOf(typeof(ChannelTestResolver), channel.ConnectionManager._resolver);
        }

        [Test]
        public void Resolver_NoMatchInServiceProvider_Error()
        {
            // Arrange
            var services = new ServiceCollection();

            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider()
            };

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost", channelOptions))!;

            // Assert
            Assert.AreEqual("No address resolver configured for the scheme 'test'.", ex.Message);
        }

        [Test]
        public void Resolver_NoServiceProvider_Error()
        {
            // Arrange & Act
            var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost"))!;

            // Assert
            Assert.AreEqual("No address resolver configured for the scheme 'test'.", ex.Message);
        }

        [TestCase(false, 80)]
        [TestCase(true, 443)]
        public void Resolver_DefaultPort_MatchesSecure(bool isSecure, int expectedPort)
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
            services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

            var handler = new TestHttpMessageHandler();
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = isSecure ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);

            // Assert
            Assert.IsInstanceOf(typeof(ChannelTestResolver), channel.ConnectionManager._resolver);

            var resolver = (ChannelTestResolver)channel.ConnectionManager._resolver;
            Assert.AreEqual(expectedPort, resolver.Options.DefaultPort);
        }

        public class ChannelTestResolverFactory : ResolverFactory
        {
            public override string Name => "test";

            public override Resolver Create(ResolverOptions options)
            {
                return new ChannelTestResolver(options);
            }
        }

        public class ChannelTestResolver : Resolver
        {
            public ChannelTestResolver(ResolverOptions options)
            {
                Options = options;
            }

            public ResolverOptions Options { get; }

            public override void Start(Action<ResolverResult> listener)
            {
                throw new NotImplementedException();
            }

            public override void Refresh()
            {
                throw new NotImplementedException();
            }
        }
#endif

        public class TestHttpMessageHandler : HttpMessageHandler
        {
            public bool Disposed { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var tcs = new TaskCompletionSource<HttpResponseMessage>();
                cancellationToken.Register(s => ((TaskCompletionSource<HttpResponseMessage>)s!).SetException(new OperationCanceledException()), tcs);
                return await tcs.Task;
            }

            protected override void Dispose(bool disposing)
            {
                Disposed = true;
            }
        }

        public class ExceptionHttpMessageHandler : HttpMessageHandler
        {
            public string ExceptionMessage { get; }

            public ExceptionHttpMessageHandler(string exceptionMessage)
            {
                ExceptionMessage = exceptionMessage;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromException<HttpResponseMessage>(new InvalidOperationException(ExceptionMessage));
            }
        }
    }
}
