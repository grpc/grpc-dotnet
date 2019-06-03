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
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GetTrailersTests
    {
        [Test]
        public async Task AsyncUnaryCall_MessageReturned_ReturnsTrailers()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Add("custom", "ABC");
                response.TrailingHeaders.Add("custom-header", "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var message = await call;
            var trailers1 = call.GetTrailers();
            var trailers2 = call.GetTrailers();

            // Assert
            Assert.AreSame(trailers1, trailers2);
            Assert.AreEqual("value", trailers1.Single(t => t.Key == "custom-header").Value);
        }

        [Test]
        public async Task AsyncUnaryCall_HeadersReturned_ReturnsTrailers()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.Headers.Add("custom", "ABC");
                response.TrailingHeaders.Add("custom-header", "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();
            var trailers = call.GetTrailers();

            // Assert
            Assert.AreEqual("value", trailers.Single(t => t.Key == "custom-header").Value);
        }

        [Test]
        public void AsyncUnaryCall_UnfinishedCall_ThrowsError()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                await tcs.Task.DefaultTimeout();
                throw new Exception("Test shouldn't reach here.");
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Can't get the call trailers because the call is not complete.", ex.Message);
        }

        [Test]
        public void AsyncUnaryCall_ErrorCall_ThrowsError()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                return Task.FromException<HttpResponseMessage>(new Exception("An error!"));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Can't get the call trailers because an error occured when making the request.", ex.Message);
            Assert.AreEqual("An error!", ex.InnerException.InnerException.Message);
        }

        [Test]
        public void AsyncClientStreamingCall_UnfinishedCall_ThrowsError()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                await tcs.Task.DefaultTimeout();
                throw new Exception("Test shouldn't reach here.");
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Can't get the call trailers because the call is not complete.", ex.Message);
        }

        [Test]
        public void AsyncServerStreamingCall_UnfinishedCall_ThrowsError()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                await tcs.Task.DefaultTimeout();
                throw new Exception("Test shouldn't reach here.");
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Can't get the call trailers because the call is not complete.", ex.Message);
        }

        [Test]
        public async Task AsyncServerStreamingCall_UnfinishedReader_ThrowsError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(
                    new HelloReply
                    {
                        Message = "Hello world 1"
                    },
                    new HelloReply
                    {
                        Message = "Hello world 2"
                    }).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var responseStream = call.ResponseStream;

            Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Can't get the call trailers because the call is not complete.", ex.Message);
        }

        [Test]
        public async Task AsyncServerStreamingCall_FinishedReader_ReturnsTrailers()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(
                    new HelloReply
                    {
                        Message = "Hello world 1"
                    },
                    new HelloReply
                    {
                        Message = "Hello world 2"
                    }).DefaultTimeout();

                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                response.TrailingHeaders.Add("custom-header", "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var responseStream = call.ResponseStream;

            Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            var trailers = call.GetTrailers();

            // Assert
            Assert.AreEqual("value", trailers.Single(t => t.Key == "custom-header").Value);
        }

        [Test]
        public async Task AsyncClientStreamingCall_CompleteWriter_ReturnsTrailers()
        {
            // Arrange
            var trailingHeadersWrittenTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var content = (PushStreamContent)request.Content;
                var stream = new SyncPointMemoryStream();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream));

                _ = Task.Run(async () =>
                {
                    // Add a response message after the client has completed
                    await content.PushComplete.DefaultTimeout();

                    var messageData = await TestHelpers.GetResponseDataAsync(new HelloReply { Message = "Hello world" }).DefaultTimeout();
                    await stream.AddDataAndWait(messageData).DefaultTimeout();
                    await stream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

                    response.TrailingHeaders.Add("custom-header", "value");
                    trailingHeadersWrittenTcs.SetResult(true);
                });

                return Task.FromResult(response);
            });

            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());
            await call.RequestStream.CompleteAsync().DefaultTimeout();
            await Task.WhenAll(call.ResponseAsync, trailingHeadersWrittenTcs.Task).DefaultTimeout();
            var trailers = call.GetTrailers();

            // Assert
            Assert.AreEqual("value", trailers.Single(t => t.Key == "custom-header").Value);
        }

        [Test]
        public void AsyncClientStreamingCall_UncompleteWriter_ThrowsError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var stream = new SyncPointMemoryStream();

                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream), grpcStatusCode: null);
                return Task.FromResult(response);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Can't get the call trailers because the call is not complete.", ex.Message);
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
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

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
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers());

            // Assert
            Assert.AreEqual("Bad gRPC response. Invalid content-type value: text/plain", ex.Message);
        }
    }
}
