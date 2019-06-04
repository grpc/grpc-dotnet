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
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Net.ClientFactory;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class GrpcHttpClientBuilderExtensionsTests
    {
        [Test]
        public void AddInterceptor_MultipleInstances_ExecutedInOrder()
        {
            // Arrange
            var list = new List<int>();

            ServiceCollection services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = new Uri("http://localhost");
                })
                .AddInterceptor(() => new CallbackInterceptor(() => list.Add(1)))
                .AddInterceptor(() => new CallbackInterceptor(() => list.Add(2)))
                .AddInterceptor(() => new CallbackInterceptor(() => list.Add(3)))
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new TestHttpMessageHandler();
                });

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Handle bad response
            Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SayHelloAsync(new HelloRequest()));

            // Assert
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(2, list[1]);
            Assert.AreEqual(3, list[2]);
        }

        [Test]
        public void AddInterceptorGeneric_MultipleInstances_ExecutedInOrder()
        {
            // Arrange
            var list = new List<int>();
            var i = 0;

            ServiceCollection services = new ServiceCollection();
            services.AddTransient<CallbackInterceptor>(s => new CallbackInterceptor(() =>
            {
                var increment = i += 2;
                list.Add(increment);
            }));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = new Uri("http://localhost");
                })
                .AddInterceptor<CallbackInterceptor>()
                .AddInterceptor<CallbackInterceptor>()
                .AddInterceptor<CallbackInterceptor>()
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new TestHttpMessageHandler();
                });

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Handle bad response
            Assert.ThrowsAsync<InvalidOperationException>(async () => await client.SayHelloAsync(new HelloRequest()));

            // Assert
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(2, list[0]);
            Assert.AreEqual(4, list[1]);
            Assert.AreEqual(6, list[2]);
        }

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage());
            }
        }
    }
}
