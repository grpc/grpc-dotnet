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
using Grpc.AspNetCore.Server.ClientFactory.Tests.TestObjects;
using Grpc.AspNetCore.Server.Features;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
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
        public void CreateClient_ServerCallContextHasValues_PropogatedDeadlineAndCancellation()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");
            var deadline = new DateTime(2000, 12, 12, 1, 1, 1, DateTimeKind.Utc);
            var cancellationToken = new CancellationTokenSource().Token;

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessorWithServerCallContext(deadline, cancellationToken));
            services.AddGrpcClient<TestGreeterClient>(o =>
            {
                o.BaseAddress = baseAddress;
            }).EnableCallContextPropagation();

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>());

            // Act
            var client = clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient));

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(baseAddress, client.CallInvoker.BaseAddress);
            Assert.AreEqual(deadline, client.CallInvoker.Deadline);
            Assert.AreEqual(cancellationToken, client.CallInvoker.CancellationToken);
        }

        [Test]
        public void CreateClient_NoHttpContext_ThrowError()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(null));
            services.AddGrpcClient<TestGreeterClient>(o =>
            {
                o.BaseAddress = baseAddress;
            }).EnableCallContextPropagation();

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>());

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient)));

            // Assert
            Assert.AreEqual("Unable to propagate server context values to the client. Can't find the current HttpContext.", ex.Message);
        }

        [Test]
        public void CreateClient_NoServerCallContextOnHttpContext_ThrowError()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddOptions();
            services.AddSingleton(CreateHttpContextAccessor(new DefaultHttpContext()));
            services.AddGrpcClient<TestGreeterClient>(o =>
            {
                o.BaseAddress = baseAddress;
            }).EnableCallContextPropagation();

            var serviceProvider = services.BuildServiceProvider();

            var clientFactory = new DefaultGrpcClientFactory(
                serviceProvider,
                serviceProvider.GetRequiredService<IHttpClientFactory>(),
                serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>());

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<TestGreeterClient>(nameof(TestGreeterClient)));

            // Assert
            Assert.AreEqual("Unable to propagate server context values to the client. Can't find the current gRPC ServerCallContext.", ex.Message);
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
