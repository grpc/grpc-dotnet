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
            var httpClient = CreateGrpcWebClient();
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var client = new AuthorizedGreeter.AuthorizedGreeterClient(channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloAsync(new HelloRequest { Name = "test" }).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Unauthenticated, ex.StatusCode);

            AssertHasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Unauthenticated', Message: 'Bad gRPC response. HTTP status code: 401'.");
        }
    }
}
