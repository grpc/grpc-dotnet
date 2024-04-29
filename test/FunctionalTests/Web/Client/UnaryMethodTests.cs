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

using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Gateway.Testing;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
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
public class UnaryMethodTests : GrpcWebFunctionalTestBase
{
    public UnaryMethodTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
     : base(grpcTestMode, endpointName)
    {
    }

    [Test]
    public async Task SendValidRequest_SuccessResponse()
    {
        // Arrage
        var httpClient = CreateGrpcWebClient();
        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient,
            LoggerFactory = LoggerFactory
        });

        var client = new EchoService.EchoServiceClient(channel);

        // Act
        var response = await client.EchoAsync(new EchoRequest { Message = "test" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("test", response.Message);
    }
}
