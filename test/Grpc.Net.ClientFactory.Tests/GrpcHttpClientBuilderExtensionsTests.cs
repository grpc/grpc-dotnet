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
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Greet;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class GrpcHttpClientBuilderExtensionsTests
    {
        [Test]
        public async Task ConfigureChannel_MaxSizeSet_ThrowMaxSizeError()
        {
            // Arrange
            var request = new HelloRequest
            {
                Name = new string('!', 1024)
            };

            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .ConfigureChannel(options =>
                {
                    options.SendMaxMessageSize = 100;
                })
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new TestHttpMessageHandler();
                });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Handle bad response
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloAsync(request).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        private class TestService
        {
            public TestService(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        [Test]
        public async Task AddInterceptor_MultipleInstances_ExecutedInOrder()
        {
            // Arrange
            var list = new List<int>();

            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .AddInterceptor(() => new CallbackInterceptor(o => list.Add(1)))
                .AddInterceptor(() => new CallbackInterceptor(o => list.Add(2)))
                .AddInterceptor(() => new CallbackInterceptor(o => list.Add(3)))
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new TestHttpMessageHandler();
                });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsNotNull(response);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(2, list[1]);
            Assert.AreEqual(3, list[2]);
        }

        [Test]
        public void AddInterceptor_OnGrpcClientFactoryWithServicesConfig_Success()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<CallbackInterceptor>(s => new CallbackInterceptor(o => { }));

            // Act
            services
                .AddGrpcClient<Greeter.GreeterClient>((s, o) => o.Address = new Uri("http://localhost"))
                .AddInterceptor<CallbackInterceptor>();

            // Assert
            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            Assert.IsNotNull(serviceProvider.GetRequiredService<Greeter.GreeterClient>());
        }

        [Test]
        public void AddInterceptor_NotFromGrpcClientFactoryAndExistingGrpcClient_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddGrpcClient<Greeter.GreeterClient>();
            var client = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(() => new CallbackInterceptor(o => { })));
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);

            ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(s => new CallbackInterceptor(o => { })));
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public void AddInterceptor_NotFromGrpcClientFactory_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            var client = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(() => new CallbackInterceptor(o => { })));
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);

            ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(s => new CallbackInterceptor(o => { })));
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public async Task AddInterceptorGeneric_MultipleInstances_ExecutedInOrder()
        {
            // Arrange
            var list = new List<int>();
            var i = 0;

            var services = new ServiceCollection();
            services.AddTransient<CallbackInterceptor>(s => new CallbackInterceptor(o =>
            {
                var increment = i += 2;
                list.Add(increment);
            }));
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .AddInterceptor<CallbackInterceptor>()
                .AddInterceptor<CallbackInterceptor>()
                .AddInterceptor<CallbackInterceptor>()
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new TestHttpMessageHandler();
                });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsNotNull(response);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual(2, list[0]);
            Assert.AreEqual(4, list[1]);
            Assert.AreEqual(6, list[2]);
        }

        [Test]
        public void AddInterceptorGeneric_NotFromGrpcClientFactory_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            var client = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor<CallbackInterceptor>());
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
        }

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Get stream from request content so gRPC client serializes request message
                _ = await request.Content.ReadAsStreamAsync();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            }
        }
    }
}
