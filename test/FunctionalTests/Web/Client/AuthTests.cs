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
using System.Threading.Tasks;
using Authorize;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Gateway.Testing;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Web.Client
{
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWeb, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http1)]
    [TestFixture(GrpcTestMode.GrpcWebText, TestServerEndpointName.Http2)]
    [TestFixture(GrpcTestMode.Grpc, TestServerEndpointName.Http2)]
    public class AuthTests : GrpcWebFunctionalTestBase
    {
        public AuthTests(GrpcTestMode grpcTestMode, TestServerEndpointName endpointName)
         : base(grpcTestMode, endpointName)
        {
        }

        [Test]
        public async Task SendUnauthenticatedRequest_UnauthenticatedErrorResponse()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                // This error can happen if the server returns an unauthorized response
                // before the client has finished sending the request content.
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorSendingMessage" &&
                    writeContext.Exception is OperationCanceledException)
                {
                    return true;
                }

                return false;
            });

            // Arrage
            var channel = CreateGrpcWebChannel();

            var client = new AuthorizedGreeter.AuthorizedGreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloAsync(new HelloRequest { Name = "test" }).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Unauthenticated, ex.StatusCode);

            AssertHasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Unauthenticated', Message: 'Bad gRPC response. HTTP status code: 401'.");
        }

        [Test]
        public async Task SendUnauthenticatedRequest_Success()
        {
            // Arrange
            var tokenResponse = await Fixture.Client.GetAsync("generateJwtToken").DefaultTimeout();
            var token = await tokenResponse.Content.ReadAsStringAsync().DefaultTimeout();

            var channel = CreateGrpcWebChannel();

            var client = new AuthorizedGreeter.AuthorizedGreeterClient(channel);

            // Act
            var metadata = new Metadata();
            metadata.Add("Authorization", $"Bearer {token}");

            var response = await client.SayHelloAsync(new HelloRequest { Name = "test" }, headers: metadata).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Hello test", response.Message);

            Assert.AreEqual("testuser", response.Claims["http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"]);
        }

        [Test]
        public async Task SendAuthHeader_ReceivedOnServer()
        {
            string? httpContextAuthorization = null;
            string? metadataAuthorization = null;
            Task<HelloReply> ReadAuthHeaderOnServer(HelloRequest request, ServerCallContext context)
            {
                httpContextAuthorization = context.GetHttpContext().Request.Headers[HeaderNames.Authorization];
                metadataAuthorization = context.RequestHeaders.GetValue("authorization");

                return Task.FromResult(new HelloReply());
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(ReadAuthHeaderOnServer);

            var channel = CreateGrpcWebChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var metadata = new Metadata();
            metadata.Add("Authorization", "123");
            var call = client.UnaryCall(new HelloRequest(), new CallOptions(headers: metadata));

            await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("123", httpContextAuthorization);
            Assert.AreEqual("123", metadataAuthorization);
        }
    }
}
