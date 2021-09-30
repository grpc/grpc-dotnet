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
using Greet;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.ClientFactory;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
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
                    options.MaxSendMessageSize = 100;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

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
            var testHttpMessageHandler = new TestHttpMessageHandler();

            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .AddInterceptor(() => new CallbackInterceptor(o => list.Add(1)))
                .AddInterceptor(() => new CallbackInterceptor(o => list.Add(2)))
                .AddInterceptor(() => new CallbackInterceptor(o => list.Add(3)))
                .ConfigurePrimaryHttpMessageHandler(() => testHttpMessageHandler);

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            var response = await client.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsTrue(testHttpMessageHandler.Invoked);
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
                .AddInterceptor<CallbackInterceptor>()
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

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

            var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(() => new CallbackInterceptor(o => { })))!;
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);

            ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(s => new CallbackInterceptor(o => { })))!;
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public void AddInterceptor_NotFromGrpcClientFactory_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            var client = services.AddHttpClient("TestClient");

            var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(() => new CallbackInterceptor(o => { })))!;
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);

            ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor(s => new CallbackInterceptor(o => { })))!;
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
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

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

            var ex = Assert.Throws<InvalidOperationException>(() => client.AddInterceptor<CallbackInterceptor>())!;
            Assert.AreEqual("AddInterceptor must be used with a gRPC client.", ex.Message);
        }

        [Test]
        public void ConfigureGrpcClientCreator_CreatorSuccess_ClientReturned()
        {
            // Arrange
            Greeter.GreeterClient? createdClient = null;

            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .ConfigureGrpcClientCreator(callInvoker =>
                {
                    createdClient = new Greeter.GreeterClient(callInvoker);
                    return createdClient;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var client = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));

            // Assert
            Assert.IsNotNull(client);
            Assert.AreEqual(createdClient, client);
        }

        [Test]
        public void ConfigureGrpcClientCreator_ServiceProviderCreatorSuccess_ClientReturned()
        {
            // Arrange
            DerivedGreeterClient? createdGreaterClient = null;

            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .ConfigureGrpcClientCreator((serviceProvider, callInvoker) =>
                {
                    createdGreaterClient = new DerivedGreeterClient(callInvoker);
                    return createdGreaterClient;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());
            services
                .AddGrpcClient<SecondGreeter.SecondGreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var greeterClient = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
            var secondGreeterClient = clientFactory.CreateClient<SecondGreeter.SecondGreeterClient>(nameof(SecondGreeter.SecondGreeterClient));

            // Assert
            Assert.IsNotNull(greeterClient);
            Assert.AreEqual(createdGreaterClient, greeterClient);
            Assert.IsNotNull(secondGreeterClient);
        }

        [Test]
        public void ConfigureGrpcClientCreator_CreatorReturnsNull_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .ConfigureGrpcClientCreator(callInvoker =>
                {
                    return null!;
                })
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient)))!;

            // Assert
            Assert.AreEqual("A null instance was returned by the configured client creator.", ex.Message);
        }

        [Test]
        public void ConfigureGrpcClientCreator_CreatorReturnsIncorrectType_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .ConfigureGrpcClientCreator(callInvoker =>
                {
                    return new object();
                })
                .ConfigurePrimaryHttpMessageHandler(() => new TestHttpMessageHandler());

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient)))!;

            // Assert
            Assert.AreEqual("The System.Object instance returned by the configured client creator is not compatible with Greet.Greeter+GreeterClient.", ex.Message);
        }

        [TestCase(InterceptorLifetime.Client, 2)]
        [TestCase(InterceptorLifetime.Channel, 1)]
        public async Task AddInterceptor_InterceptorLifetime_InterceptorCreatedCountCorrect(InterceptorLifetime lifetime, int callCount)
        {
            // Arrange
            var testHttpMessageHandler = new TestHttpMessageHandler();

            var interceptorCreatedCount = 0;
            var services = new ServiceCollection();
            services.AddTransient<CallbackInterceptor>(s =>
            {
                interceptorCreatedCount++;
                return new CallbackInterceptor(o => { });
            });

            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .AddInterceptor<CallbackInterceptor>(lifetime)
                .ConfigurePrimaryHttpMessageHandler(() => testHttpMessageHandler);

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var clientFactory = serviceProvider.GetRequiredService<GrpcClientFactory>();

            var client1 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
            await client1.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            var client2 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
            await client2.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(callCount, interceptorCreatedCount);
        }

        [TestCase(1)]
        [TestCase(2)]
        public async Task AddInterceptor_ClientLifetimeInScope_InterceptorCreatedCountCorrect(int scopes)
        {
            // Arrange
            var testHttpMessageHandler = new TestHttpMessageHandler();

            var interceptorCreatedCount = 0;
            var services = new ServiceCollection();
            services.AddScoped<CallbackInterceptor>(s =>
            {
                interceptorCreatedCount++;
                return new CallbackInterceptor(o => { });
            });

            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.Address = new Uri("http://localhost");
                })
                .AddInterceptor<CallbackInterceptor>()
                .ConfigurePrimaryHttpMessageHandler(() => testHttpMessageHandler);

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            for (var i = 0; i < scopes; i++)
            {
                await MakeCallsInScope(serviceProvider);
            }

            // Assert
            Assert.AreEqual(scopes, interceptorCreatedCount);

            static async Task MakeCallsInScope(ServiceProvider rootServiceProvider)
            {
                var scope = rootServiceProvider.CreateScope();

                var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();

                var client1 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
                await client1.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();

                var client2 = clientFactory.CreateClient<Greeter.GreeterClient>(nameof(Greeter.GreeterClient));
                await client2.SayHelloAsync(new HelloRequest()).ResponseAsync.DefaultTimeout();
            }
        }

        private class DerivedGreeterClient : Greeter.GreeterClient
        {
            public DerivedGreeterClient(CallInvoker callInvoker) : base(callInvoker)
            {
            }
        }

        private class TestHttpMessageHandler : HttpMessageHandler
        {
            public bool Invoked { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Invoked = true;

                // Get stream from request content so gRPC client serializes request message
                _ = await request.Content!.ReadAsStreamAsync();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            }
        }
    }
}
