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

using System.Net;
using System.Net.Security;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class ClientFactoryTests : FunctionalTestBase
{
    [Test]
    public async Task ClientFactory_CreateMultipleClientsAndMakeCalls_Success()
    {
        // Arrange
        Task<HelloReply> UnaryCall(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = $"Hello {request.Name}" });
        }
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryCall);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
        serviceCollection
            .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
            {
                options.Address = Fixture.GetUrl(TestServerEndpointName.Http2WithTls);
            })
            .ConfigureGrpcClientCreator(invoker =>
            {
                return TestClientFactory.Create(invoker, method);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (____, ___, __, _) => true
                    }
                };
            });
        var services = serviceCollection.BuildServiceProvider();

        // Act 1
        var client1 = services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();
        var call1 = client1.UnaryCall(new HelloRequest { Name = "world" });
        var response1 = await call1.ResponseAsync.DefaultTimeout();

        // Assert 1
        Assert.AreEqual("Hello world", response1.Message);

        // Act 2
        var client2 = services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();
        var call2 = client2.UnaryCall(new HelloRequest { Name = "world" });
        var response2 = await call2.ResponseAsync.DefaultTimeout();

        // Assert 2
        Assert.AreEqual("Hello world", response2.Message);
    }

#if NET7_0_OR_GREATER
    [Test]
    [RequireHttp3]
    public async Task ClientFactory_Http3_Success()
    {
        // Arrange
        Task<HelloReply> UnaryCall(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = $"Hello {request.Name}" });
        }
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryCall);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
        serviceCollection
            .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
            {
                options.Address = Fixture.GetUrl(TestServerEndpointName.Http3WithTls);
            })
            .ConfigureGrpcClientCreator(invoker =>
            {
                return TestClientFactory.Create(invoker, method);
            })
            .ConfigureChannel(options =>
            {
                options.HttpVersion = HttpVersion.Version30;
                options.HttpVersionPolicy = HttpVersionPolicy.RequestVersionExact;
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new SocketsHttpHandler
                {
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (____, ___, __, _) => true
                    }
                };
            });
        var services = serviceCollection.BuildServiceProvider();

        // Act
        var client1 = services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();
        var call1 = client1.UnaryCall(new HelloRequest { Name = "world" });
        var response1 = await call1.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Hello world", response1.Message);
    }
#endif
}
