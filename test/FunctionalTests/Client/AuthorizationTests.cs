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
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class AuthorizationTests : FunctionalTestBase
{
    [Test]
    public async Task Client_CallCredentials_RoundtripToken()
    {
        // Arrange
        string? authorization = null;
        Task<HelloReply> UnaryTelemetryHeader(HelloRequest request, ServerCallContext context)
        {
            authorization = context.RequestHeaders.GetValue("authorization");

            return Task.FromResult(new HelloReply());
        }
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryTelemetryHeader);

        var token = "token!";
        var credentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            if (!string.IsNullOrEmpty(token))
            {
                metadata.Add("Authorization", $"Bearer {token}");
            }
            return Task.CompletedTask;
        });

        var options = new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory,
            Credentials = ChannelCredentials.Create(new SslCredentials(), credentials),
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            }
        };

        var channel = GrpcChannel.ForAddress(Fixture.GetUrl(TestServerEndpointName.Http2WithTls), options);
        var client = TestClientFactory.Create<HelloRequest, HelloReply>(channel, method);

        var call = client.UnaryCall(new HelloRequest { Name = "world" });

        // Act
        await call.ResponseAsync.DefaultTimeout();

        Assert.AreEqual("Bearer token!", authorization);
    }

    [Test]
    public async Task ClientFactory_CallCredentials_RoundtripToken()
    {
        // Arrange
        string? authorization = null;
        Task<HelloReply> UnaryTelemetryHeader(HelloRequest request, ServerCallContext context)
        {
            authorization = context.RequestHeaders.GetValue("authorization");

            return Task.FromResult(new HelloReply());
        }
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryTelemetryHeader);

        var token = "token!";
        var credentials = CallCredentials.FromInterceptor((context, metadata) =>
        {
            if (!string.IsNullOrEmpty(token))
            {
                metadata.Add("Authorization", $"Bearer {token}");
            }
            return Task.CompletedTask;
        });

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
        serviceCollection
            .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
            {
                options.Address = Fixture.GetUrl(TestServerEndpointName.Http2WithTls);
            })
            .ConfigureChannel(channel =>
            {
                channel.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials);
            })
            .ConfigureGrpcClientCreator(invoker =>
            {
                return TestClientFactory.Create(invoker, method);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                };
            });
        var services = serviceCollection.BuildServiceProvider();

        var client = services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();

        // Act
        var call = client.UnaryCall(new HelloRequest { Name = "world" });

        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Bearer token!", authorization);
    }
}
