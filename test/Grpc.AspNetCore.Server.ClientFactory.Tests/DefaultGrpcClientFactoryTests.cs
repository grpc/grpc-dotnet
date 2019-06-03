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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.Server.ClientFactory.Internal;
using Grpc.AspNetCore.Server.Features;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class DefaultGrpcClientFactoryTests
    {
        [Test]
        public void CreateClient_MatchingConfigurationBasedOnTypeName_ReturnConfiguration()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor());
            services.AddGrpcClient<TestGreeterClient>(o => o.BaseAddress = baseAddress);

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>());

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(baseAddress, client.CallInvoker.BaseAddress);
        }

        [Test]
        public void CreateClient_MatchingConfigurationBasedOnCustomName_ReturnConfiguration()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor());
            services.AddGrpcClient<TestGreeterClient>("Custom", o => o.BaseAddress = baseAddress);

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>());

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>("Custom");

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(baseAddress, client.CallInvoker.BaseAddress);
        }

        [Test]
        public void CreateClient_ServerCallContextHasValues_PropogatedDeadlineAndCancellation()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");
            var deadline = new DateTime(2000, 12, 12, 1, 1, 1, DateTimeKind.Utc);
            var cancellationToken = new CancellationTokenSource().Token;

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(deadline, cancellationToken));
            services.AddGrpcClient<TestGreeterClient>(o =>
            {
                o.BaseAddress = baseAddress;
                o.PropagateDeadline = true;
                o.PropagateCancellationToken = true;
            });

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>());

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(baseAddress, client.CallInvoker.BaseAddress);
            Assert.AreEqual(deadline, client.CallInvoker.Deadline);
            Assert.AreEqual(cancellationToken, client.CallInvoker.CancellationToken);
        }

        [Test]
        public void CreateClient_NoMatchingConfiguration_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor());
            services.AddGrpcClient<TestGreeterClient>(o => { });

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientOptions>>());

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greet.Greeter.GreeterClient>("Test"));

            // Assert
            Assert.AreEqual("No gRPC client configured with name 'Test'.", ex.Message);
        }

        private IHttpContextAccessor CreateHttpContextAccessor(DateTime deadline = default, CancellationToken cancellationToken = default)
        {
            var test = new TestHttpContextAccessor(new DefaultHttpContext());

            var serverCallContext = new TestServerCallContext(deadline, cancellationToken);
            var serverCallContextFeature = new TestServerCallContextFeature(serverCallContext);
            test.HttpContext.Features.Set<IServerCallContextFeature>(serverCallContextFeature);

            return test;
        }

        private class TestServerCallContext : ServerCallContext
        {
            public TestServerCallContext(DateTime deadline, CancellationToken cancellationToken)
            {
                DeadlineCore = deadline;
                CancellationTokenCore = cancellationToken;
            }

            protected override string? MethodCore { get; }
            protected override string? HostCore { get; }
            protected override string? PeerCore { get; }
            protected override DateTime DeadlineCore { get; }
            protected override Metadata? RequestHeadersCore { get; }
            protected override CancellationToken CancellationTokenCore { get; }
            protected override Metadata? ResponseTrailersCore { get; }
            protected override Status StatusCore { get; set; }
            protected override WriteOptions? WriteOptionsCore { get; set; }
            protected override AuthContext? AuthContextCore { get; }

            protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions options)
            {
                throw new NotImplementedException();
            }

            protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders)
            {
                throw new NotImplementedException();
            }
        }

        public class TestGreeterClient : Greeter.GreeterClient
        {
            public TestGreeterClient(CallInvoker callInvoker) : base(callInvoker)
            {
                CallInvoker = (HttpClientCallInvoker)callInvoker;
            }

            public new HttpClientCallInvoker CallInvoker { get; }
        }

        private class TestServerCallContextFeature : IServerCallContextFeature
        {
            public TestServerCallContextFeature(ServerCallContext serverCallContext)
            {
                ServerCallContext = serverCallContext;
            }

            public ServerCallContext ServerCallContext { get; }
        }

        private class TestHttpContextAccessor : IHttpContextAccessor
        {
            public TestHttpContextAccessor(HttpContext httpContext)
            {
                HttpContext = httpContext;
            }

            public HttpContext HttpContext { get; set; }
        }
    }
}
