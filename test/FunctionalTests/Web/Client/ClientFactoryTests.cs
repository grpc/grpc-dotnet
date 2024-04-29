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
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client;

[TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http1)]
[TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http2)]
#if NET7_0_OR_GREATER
[TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http3WithTls)]
#endif
[TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http1)]
[TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http2)]
#if NET7_0_OR_GREATER
[TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http3WithTls)]
#endif
[TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http2)]
#if NET7_0_OR_GREATER
[TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http3WithTls)]
#endif
public class ClientFactoryTests : GrpcWebFunctionalTestBase
{
    public ClientFactoryTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
     : base(grpcTestMode, endpointName)
    {
    }

    [Test]
    public async Task CreateMultipleClientsAndSendValidRequest_SuccessResponse()
    {
        // Arrange
        Task<HelloReply> UnaryCall(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = $"Hello {request.Name}" });
        }
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryCall);

        var clientHandlerResult = CreateGrpcWebHandler();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
        serviceCollection
            .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
            {
                options.Address = Fixture.GetUrl(EndpointName);
            })
            .ConfigureGrpcClientCreator(invoker =>
            {
                return TestClientFactory.Create(invoker, method);
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                return CreateGrpcWebHandler().handler;
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
}
