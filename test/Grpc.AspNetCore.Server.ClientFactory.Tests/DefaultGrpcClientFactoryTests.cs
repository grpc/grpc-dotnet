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
using Grpc.AspNetCore.Server.ClientFactory.Tests.TestObjects;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Tests.Shared;
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
        public void EnableCallContextPropagation_NotFromGrpcClientFactory_ThrowError()
        {
            var services = new ServiceCollection();
            var clientBuilder = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => clientBuilder.EnableCallContextPropagation());
            Assert.AreEqual("EnableCallContextPropagation must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public void EnableCallContextPropagation_NotFromGrpcClientFactoryAndExistingGrpcClient_ThrowError()
        {
            var services = new ServiceCollection();
            services.AddGrpcClient<Greeter.GreeterClient>(o => { });
            var clientBuilder = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => clientBuilder.EnableCallContextPropagation());
            Assert.AreEqual("EnableCallContextPropagation must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public async Task CreateClient_ServerCallContextHasValues_PropogatedDeadlineAndCancellation()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");
            var deadline = DateTime.UtcNow.AddDays(1);
            var cancellationToken = new CancellationTokenSource().Token;

            CallOptions options = default;

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessorWithServerCallContext(deadline, cancellationToken));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = baseAddress;
                })
                .EnableCallContextPropagation()
                .AddInterceptor(() => new CallbackInterceptor(o => options = o))
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>());
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(deadline, options.Deadline);
            Assert.AreEqual(cancellationToken, options.CancellationToken);
        }

        [Test]
        public async Task CreateClient_NoHttpContext_ThrowError()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(null));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = baseAddress;
                })
                .EnableCallContextPropagation()
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>());
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => client.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Unable to propagate server context values to the call. Can't find the current HttpContext.", ex.Message);
        }

        [Test]
        public async Task CreateClient_NoServerCallContextOnHttpContext_ThrowError()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(new DefaultHttpContext()));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = baseAddress;
                })
                .EnableCallContextPropagation()
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>());
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => client.SayHelloAsync(new HelloRequest(), new CallOptions()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Unable to propagate server context values to the call. Can't find the current gRPC ServerCallContext.", ex.Message);
        }

        private IHttpContextAccessor CreateHttpContextAccessor(HttpContext? httpContext)
        {
            return new TestHttpContextAccessor(httpContext);
        }

        private IHttpContextAccessor CreateHttpContextAccessorWithServerCallContext(DateTime deadline = default, CancellationToken cancellationToken = default)
        {
            var httpContext = new DefaultHttpContext();
            var serverCallContext = new TestServerCallContext(deadline, cancellationToken);
            var serverCallContextFeature = new TestServerCallContextFeature(serverCallContext);
            httpContext.Features.Set<IServerCallContextFeature>(serverCallContextFeature);

            return CreateHttpContextAccessor(httpContext);
        }

        private class TestHttpContextAccessor : IHttpContextAccessor
        {
            public TestHttpContextAccessor(HttpContext? httpContext)
            {
                HttpContext = httpContext;
            }

            public HttpContext? HttpContext { get; set; }
        }
    }
}
