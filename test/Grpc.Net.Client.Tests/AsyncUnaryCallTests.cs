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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class AsyncUnaryCallTests
    {
        [Test]
        public async Task AsyncUnaryCall_Success_HttpRequestMessagePopulated()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual(new Version(2, 0), httpRequestMessage!.Version);
            Assert.AreEqual(HttpMethod.Post, httpRequestMessage.Method);
            Assert.AreEqual(new Uri("https://localhost/ServiceName/MethodName"), httpRequestMessage.RequestUri);
            Assert.AreEqual(new MediaTypeHeaderValue("application/grpc"), httpRequestMessage.Content?.Headers?.ContentType);
            Assert.AreEqual(GrpcProtocolConstants.TEHeaderValue, httpRequestMessage.Headers.TE.Single().Value);
            Assert.AreEqual("identity,gzip", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());

            var userAgent = httpRequestMessage.Headers.UserAgent.Single()!;
            Assert.AreEqual("grpc-dotnet", userAgent.Product?.Name);
            Assert.IsTrue(!string.IsNullOrEmpty(userAgent.Product?.Version));

            // Santity check that the user agent doesn't have the git hash in it and isn't too long.
            // Sending a long user agent with each call has performance implications.
            Assert.IsFalse(userAgent.Product!.Version!.Contains('+'));
            Assert.IsTrue(userAgent.Product!.Version!.Length <= 10);
        }

        [Test]
        public async Task AsyncUnaryCall_Success_RequestContentSent()
        {
            // Arrange
            HttpContent? content = null;

            var handler = TestHttpMessageHandler.Create(async request =>
            {
                content = request.Content;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(handler, "http://localhost");

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(content);

            var requestContent = await content!.ReadAsStreamAsync().DefaultTimeout();
            var requestMessage = await StreamSerializationHelper.ReadMessageAsync(
                requestContent,
                ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                GrpcProtocolConstants.IdentityGrpcEncoding,
                maximumMessageSize: null,
                GrpcProtocolConstants.DefaultCompressionProviders,
                singleMessage: true,
                CancellationToken.None).DefaultTimeout();

            Assert.AreEqual("World", requestMessage!.Name);
        }

        [Test]
        public async Task AsyncUnaryCall_NonOkStatusTrailer_ThrowRpcError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()), StatusCode.Unimplemented);
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_SuccessTrailersOnly_ThrowNoMessageError()
        {
            // Arrange
            HttpResponseMessage? responseMessage = null;
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()), grpcStatusCode: null);
                responseMessage.Headers.Add(GrpcProtocolConstants.StatusTrailer, StatusCode.OK.ToString("D"));
                responseMessage.Headers.Add(GrpcProtocolConstants.MessageTrailer, "Detail!");
                return Task.FromResult(responseMessage);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var headers = await call.ResponseHeadersAsync.DefaultTimeout();
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            // Assert
            Assert.NotNull(responseMessage);
            Assert.IsFalse(responseMessage!.TrailingHeaders().Any()); // sanity check that there are no trailers

            Assert.AreEqual(StatusCode.Internal, ex.Status.StatusCode);
            Assert.AreEqual("Failed to deserialize response message.", ex.Status.Detail);

            Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
            Assert.AreEqual("Failed to deserialize response message.", call.GetStatus().Detail);

            Assert.AreEqual(0, headers.Count);
            Assert.AreEqual(0, call.GetTrailers().Count);
        }
    }
}
