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
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class ResponseAsyncTests
    {
        [Test]
        public async Task AsyncUnaryCall_AwaitMultipleTimes_SameMessageReturned()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await TestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            var response1 = await call;
            var response2 = await call;
            var response3 = await call.ResponseAsync.DefaultTimeout();
            var response4 = await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Hello world", response1.Message);

            Assert.AreEqual(response1, response2);
            Assert.AreEqual(response1, response3);
            Assert.AreEqual(response1, response4);
        }

        [Test]
        public async Task AsyncUnaryCall_DisposeAfterHeadersAndBeforeMessage_ThrowsError()
        {
            // Arrange
            var stream = new SyncPointMemoryStream();

            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream));
                response.Headers.Add("custom", "value!");
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
            var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();
            call.Dispose();

            // Assert
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await call.ResponseAsync.DefaultTimeout());

            var header = responseHeaders.Single(h => h.Key == "custom");
            Assert.AreEqual("value!", header.Value);
        }

        [Test]
        public void AsyncUnaryCall_ErrorSendingRequest_ThrowsError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                return Task.FromException<HttpResponseMessage>(new Exception("An error!"));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var ex = Assert.CatchAsync<Exception>(() => call.ResponseAsync);

            // Assert
            Assert.AreEqual("An error!", ex.Message);
        }

        [Test]
        public void AsyncClientStreamingCall_NotFoundStatus_ThrowsError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var response = ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseAsync.DefaultTimeout());

            // Assert
            Assert.AreEqual("Bad gRPC response. Expected HTTP status code 200. Got status code: 404", ex.Message);
        }

        [Test]
        public void AsyncClientStreamingCall_InvalidContentType_ThrowsError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseAsync.DefaultTimeout());

            // Assert
            Assert.AreEqual("Bad gRPC response. Invalid content-type value: text/plain", ex.Message);
        }
    }
}
