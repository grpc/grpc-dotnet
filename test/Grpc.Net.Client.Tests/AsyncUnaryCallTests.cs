﻿#region Copyright notice and license

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
            Assert.AreEqual(new MediaTypeHeaderValue("application/grpc"), httpRequestMessage.Content.Headers.ContentType);
            Assert.AreEqual(GrpcProtocolConstants.TEHeader, httpRequestMessage.Headers.TE.Single());
            Assert.AreEqual("identity,gzip", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());

            var userAgent = httpRequestMessage.Headers.UserAgent.Single();
            Assert.AreEqual(GrpcProtocolConstants.UserAgentHeader, userAgent);
            Assert.AreEqual("grpc-dotnet", userAgent.Product.Name);
            Assert.IsTrue(!string.IsNullOrEmpty(userAgent.Product.Version));
        }

        [Test]
        public async Task AsyncUnaryCall_Success_RequestContentSent()
        {
            // Arrange
            HttpContent? content = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                content = request.Content;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(content);

            var requestContent = await content!.ReadAsStreamAsync().DefaultTimeout();
            var requestMessage = await requestContent.ReadSingleMessageAsync(NullLogger.Instance, ClientTestHelpers.ServiceMethod.RequestMarshaller.Deserializer, GrpcProtocolConstants.IdentityGrpcEncoding, CancellationToken.None).DefaultTimeout();

            Assert.AreEqual("World", requestMessage.Name);
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
        public async Task AsyncUnaryCall_HttpClientBaseAddressNotSet_ThrowError()
        {
            // Arrange
            var httpClient = new HttpClient();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Unable to send the gRPC call because no server address has been configured. " +
                "Set HttpClient.BaseAddress on the HttpClient used to created to gRPC client.", ex.Message);
        }
    }
}
