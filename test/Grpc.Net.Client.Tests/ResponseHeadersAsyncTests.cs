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
using System.Text;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class ResponseHeadersAsyncTests
    {
        [Test]
        public async Task AsyncUnaryCall_Success_ResponseHeadersPopulated()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Server.Add(new ProductInfoHeaderValue("TestName", "1.0"));
                response.Headers.Add("custom", "ABC");
                response.Headers.Add("binary-bin", Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello world")));
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var responseHeaders1 = await call.ResponseHeadersAsync.DefaultTimeout();
            var responseHeaders2 = await call.ResponseHeadersAsync.DefaultTimeout();

            // Assert
            Assert.AreSame(responseHeaders1, responseHeaders2);

            var header = responseHeaders1.Single(h => h.Key == "server");
            Assert.AreEqual("TestName/1.0", header.Value);

            header = responseHeaders1.Single(h => h.Key == "custom");
            Assert.AreEqual("ABC", header.Value);

            header = responseHeaders1.Single(h => h.Key == "binary-bin");
            Assert.AreEqual(true, header.IsBinary);
            CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("Hello world"), header.ValueBytes);
        }

        [Test]
        public async Task AsyncClientStreamingCall_Success_ResponseHeadersPopulated()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Add("custom", "ABC");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

            // Assert
            var header = responseHeaders.Single(h => h.Key == "custom");
            Assert.AreEqual("ABC", header.Value);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_Success_ResponseHeadersPopulated()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Add("custom", "ABC");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

            // Assert
            var header = responseHeaders.Single(h => h.Key == "custom");
            Assert.AreEqual("ABC", header.Value);
        }

        [Test]
        public async Task AsyncServerStreamingCall_Success_ResponseHeadersPopulated()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Add("custom", "ABC");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

            // Assert
            var header = responseHeaders.Single(h => h.Key == "custom");
            Assert.AreEqual("ABC", header.Value);
        }

        [Test]
        public void AsyncServerStreamingCall_ErrorSendingRequest_ReturnsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                return Task.FromException<HttpResponseMessage>(new Exception("An error!"));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var ex = Assert.CatchAsync<Exception>(() => call.ResponseHeadersAsync);

            // Assert
            Assert.AreEqual("An error!", ex.Message);
        }

        [Test]
        public async Task AsyncServerStreamingCall_DisposeBeforeHeadersReceived_ReturnsError()
        {
            // Arrange
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var httpClient = ClientTestHelpers.CreateTestClient(async (request, ct) =>
            {
                await tcs.Task.DefaultTimeout();
                ct.ThrowIfCancellationRequested();
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Add("custom", "ABC");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            call.Dispose();
            tcs.TrySetResult(true);

            // Assert
            await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => call.ResponseHeadersAsync).DefaultTimeout();
        }

        [Test]
        public async Task AsyncClientStreamingCall_NotFoundStatus_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                var response = ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.ResponseHeadersAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Bad gRPC response. Expected HTTP status code 200. Got status code: 404", ex.Message);
        }

        [Test]
        public async Task AsyncClientStreamingCall_InvalidContentType_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK);
                response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.ResponseHeadersAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Bad gRPC response. Invalid content-type value: text/plain", ex.Message);
        }
    }
}
