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
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        public void CreateClient_Default_InternalHttpClientHasInfiniteTimeout()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>(o => o.Address = new Uri("http://localhost"));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.AreEqual(Timeout.InfiniteTimeSpan, ((HttpClient)client.CallInvoker.Channel.HttpInvoker).Timeout);
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
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

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
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

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
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>("Test"));

            // Assert
            Assert.AreEqual("No gRPC client configured with name 'Test'.", ex.Message);
        }

        [Test]
        public void CreateClient_NoAddress_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>();

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient)));

            // Assert
            Assert.AreEqual("Could not resolve the address for gRPC client 'GreeterClient'.", ex.Message);
        }

        [Test]
        public void CreateClient_AddressSpecifiedOnHttpClientFactory_UseHttpClientFactoryAddress()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<TestGreeterClient>()
                .ConfigureHttpClient(options => options.BaseAddress = new Uri("http://contoso"));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = CreateGrpcClientFactory(serviceProvider);

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.AreEqual("http://contoso", client.CallInvoker.Channel.Address.OriginalString);
        }

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
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));
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
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));
            services
                .AddGrpcClient<TestGreeterClient>("adventureworks", options =>
                {
                    options.Address = new Uri("http://adventureworks");
                })
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

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
                CallInvoker = (HttpClientCallInvoker)callInvoker;
            }

            public new HttpClientCallInvoker CallInvoker { get; }
        }

        private class TestHttpContextAccessor : IHttpContextAccessor
        {
            public TestHttpContextAccessor(HttpContext httpContext)
            {
                HttpContext = httpContext;
            }

            public HttpContext? HttpContext { get; set; }
        }

        public class TestLoggerProvider : ILoggerProvider
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

        private static DefaultGrpcClientFactory CreateGrpcClientFactory(ServiceProvider serviceProvider)
        {
            return new DefaultGrpcClientFactory(serviceProvider,
                serviceProvider.GetRequiredService<GrpcCallInvokerFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>(),
                serviceProvider.GetRequiredService<IHttpClientFactory>());
        }
    }
}
