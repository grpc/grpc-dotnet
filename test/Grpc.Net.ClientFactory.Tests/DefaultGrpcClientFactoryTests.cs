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

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class DefaultGrpcClientFactoryTests
    {
        [Test]
        public void CreateClient_Default_DefaultInvokerSet()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>(o => o.Address = new Uri("http://localhost"))
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.IsInstanceOf(typeof(HttpMessageInvoker), client.CallInvoker.Channel.HttpInvoker);
        }

        [Test]
        public void CreateClient_MatchingConfigurationBasedOnTypeName_ReturnConfiguration()
        {
            // Arrange
            var address = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services
                .AddGrpcClient<TestGreeterClient>(o => o.Address = address)
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(address, client.CallInvoker.Channel.Address);
        }

        [Test]
        public void CreateClient_MatchingConfigurationBasedOnCustomName_ReturnConfiguration()
        {
            // Arrange
            var address = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services
                .AddGrpcClient<TestGreeterClient>("Custom", o => o.Address = address)
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>("Custom");

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(address, client.CallInvoker.Channel.Address);
        }

        [Test]
        public void CreateClient_NoMatchingConfiguration_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddOptions();
            services
                .AddGrpcClient<TestGreeterClient>()
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>("Test"))!;

            // Assert
            Assert.AreEqual("No gRPC client configured with name 'Test'.", ex.Message);
        }

        [Test]
        public void CreateClient_NoAddress_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new NullHttpHandler());

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>("CustomName"))!;

            // Assert
            Assert.AreEqual(@"Could not resolve the address for gRPC client 'CustomName'. Set an address when registering the client: services.AddGrpcClient<GreeterClient>(o => o.Address = new Uri(""https://localhost:5001""))", ex.Message);
        }

        [Test]
        public void CreateClient_ConfigureHttpClient_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>()
                .ConfigureHttpClient(options => options.BaseAddress = new Uri("http://contoso"))
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new NullHttpHandler();
                });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient)))!;

            // Assert
            Assert.AreEqual("The ConfigureHttpClient method is not supported when creating gRPC clients. Unable to create client with name 'TestGreeterClient'.", ex.Message);
        }

#if NET472
        [Test]
        public void CreateClient_NoPrimaryHandlerNetStandard_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>(o => o.Address = new Uri("https://localhost"));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var ex = Assert.Throws<PlatformNotSupportedException>(() => clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient)))!;

            // Assert
            Assert.AreEqual(@"gRPC requires extra configuration on .NET implementations that don't support gRPC over HTTP/2. An HTTP provider must be specified using GrpcChannelOptions.HttpHandler.The configured HTTP provider must either support HTTP/2 or be configured to use gRPC-Web. See https://aka.ms/aspnet/grpc/netstandard for details.", ex.Message);
        }
#endif

#if NET5_0_OR_GREATER
        [Test]
        public void CreateClient_NoPrimaryHandlerNet5OrLater_SocketsHttpHandlerConfigured()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>(o => o.Address = new Uri("https://localhost"));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var handlerFactory = serviceProvider.GetRequiredService<IHttpMessageHandlerFactory>();
            var handler = handlerFactory.CreateHandler(nameof(TestGreeterClient));

            // Assert
            var hasSocketsHttpHandler = false;
            HttpMessageHandler? currentHandler = handler;
            while (currentHandler is DelegatingHandler delegatingHandler)
            {
                currentHandler = delegatingHandler.InnerHandler;

                if (currentHandler?.GetType() == typeof(SocketsHttpHandler))
                {
                    hasSocketsHttpHandler = true;
                    break;
                }
            }

            Assert.IsTrue(hasSocketsHttpHandler);
        }
#endif

        [Test]
        public async Task CreateClient_LoggingSetup_ClientLogsToTestSink()
        {
            // Arrange
            var testSink = new TestSink();

            var services = new ServiceCollection();
            var clientBuilder = services
                .AddGrpcClient<TestGreeterClient>("contoso", options =>
                {
                    options.Address = new Uri("http://contoso");
                })
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));
            services.AddLogging(configure => configure.SetMinimumLevel(LogLevel.Trace));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, TestLoggerProvider>(s => new TestLoggerProvider(testSink, true)));

            var provider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            var contosoClient = clientFactory.CreateClient<TestGreeterClient>("contoso");

            var response = await contosoClient.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("http://contoso", contosoClient.CallInvoker.Channel.Address.OriginalString);

            Assert.IsTrue(testSink.Writes.Any(w => w.EventId.Name == "StartingCall"));
        }

        [Test]
        public void CreateClient_MultipleNamedClients_ReturnMatchingClient()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>("contoso", options =>
                {
                    options.Address = new Uri("http://contoso");
                })
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));
            services
                .AddGrpcClient<TestGreeterClient>("adventureworks", options =>
                {
                    options.Address = new Uri("http://adventureworks");
                })
                .ConfigurePrimaryHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var provider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            var contosoClient = clientFactory.CreateClient<TestGreeterClient>("contoso");
            var adventureworksClient = clientFactory.CreateClient<TestGreeterClient>("adventureworks");

            // Assert
            Assert.AreEqual("http://contoso", contosoClient.CallInvoker.Channel.Address.OriginalString);
            Assert.AreEqual("http://adventureworks", adventureworksClient.CallInvoker.Channel.Address.OriginalString);
        }

        internal class TestGreeterClient : Greeter.GreeterClient
        {
            public TestGreeterClient(CallInvoker callInvoker) : base(callInvoker)
            {
                var lifetimeTrackingCallInvoker = (LifetimeTrackingCallInvoker)callInvoker;
                CallInvoker = (HttpClientCallInvoker)lifetimeTrackingCallInvoker.InnerInvoker;
            }

            public new HttpClientCallInvoker CallInvoker { get; }
        }

        public sealed class TestLoggerProvider : ILoggerProvider
        {
            private readonly Func<LogLevel, bool> _filter;

            public TestLoggerProvider(TestSink testSink, bool isEnabled) :
                this(testSink, _ => isEnabled)
            {
            }

            public TestLoggerProvider(TestSink testSink, Func<LogLevel, bool> filter)
            {
                Sink = testSink;
                _filter = filter;
            }

            public TestSink Sink { get; }

            public bool DisposeCalled { get; private set; }

            public ILogger CreateLogger(string categoryName)
            {
                return new TestLogger(categoryName, Sink, _filter);
            }

            public void Dispose()
            {
                DisposeCalled = true;
            }
        }

        private class NullHttpHandler : DelegatingHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage());
            }
        }

        private static DefaultGrpcClientFactory CreateGrpcClientFactory(ServiceProvider serviceProvider)
        {
            return new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<GrpcCallInvokerFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>());
        }
    }
}
