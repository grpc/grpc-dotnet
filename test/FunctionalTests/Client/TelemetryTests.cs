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
using NUnit.Framework;
using System.Net.Http;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class TelemetryTests : FunctionalTestBase
    {
        [Test]
        public async Task InternalHandler_UnaryCall_TelemetryHeaderSentWithRequest()
        {
            await TestTelemetryHeaderIsSet(handler: null);
        }

#if NET5_0
        [Test]
        public async Task SocketsHttpHandler_UnaryCall_TelemetryHeaderSentWithRequest()
        {
            await TestTelemetryHeaderIsSet(handler: new SocketsHttpHandler());
        }

        [Test]
        public async Task SocketsHttpHandlerWrapped_UnaryCall_TelemetryHeaderSentWithRequest()
        {
            await TestTelemetryHeaderIsSet(handler: new TestDelegatingHandler(new SocketsHttpHandler()));
        }

        private class TestDelegatingHandler : DelegatingHandler
        {
            public TestDelegatingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
            {
            }
        }
#endif

        private async Task TestTelemetryHeaderIsSet(HttpMessageHandler? handler)
        {
            string? telemetryHeader = null;
            Task<HelloReply> UnaryTelemetryHeader(HelloRequest request, ServerCallContext context)
            {
                telemetryHeader = context.RequestHeaders.GetValue(
#if NET5_0
                    "traceparent"
#else
                    "request-id"
#endif
                    );

                return Task.FromResult(new HelloReply());
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryTelemetryHeader);

            var options = new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = handler
            };

            // Want to test the behavior of the default, internally created handler.
            // Only supply the URL to a manually created GrpcChannel.
            var channel = GrpcChannel.ForAddress(Fixture.GetUrl(TestServerEndpointName.Http2), options);
            var client = TestClientFactory.Create(channel, method);

            // Act
            await client.UnaryCall(new HelloRequest());

            // Assert
            Assert.IsNotNull(telemetryHeader);
        }
    }
}
