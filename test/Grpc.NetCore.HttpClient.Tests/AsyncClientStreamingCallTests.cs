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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.NetCore.HttpClient.Internal;
using Grpc.NetCore.HttpClient.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.NetCore.HttpClient.Tests
{
    [TestFixture]
    public class AsyncClientStreamingCallTests
    {
        [Test]
        public async Task AsyncClientStreamingCall_Success_HttpRequestMessagePopulated()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await TestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var response = await call;

            // Assert
            Assert.AreEqual("Hello world", response.Message);

            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual(new Version(2, 0), httpRequestMessage!.Version);
            Assert.AreEqual(HttpMethod.Post, httpRequestMessage.Method);
            Assert.AreEqual(new Uri("https://localhost/ServiceName/MethodName"), httpRequestMessage.RequestUri);
            Assert.AreEqual(new MediaTypeHeaderValue("application/grpc"), httpRequestMessage.Content.Headers.ContentType);
        }

        [Test]
        public async Task AsyncClientStreamingCall_Success_RequestContentSent()
        {
            // Arrange
            PushStreamContent? content = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                content = (PushStreamContent)request.Content;
                await content.PushComplete.DefaultTimeout();

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await TestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            Assert.IsNotNull(call);
            Assert.IsNotNull(content);

            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

            var streamTask = content!.ReadAsStreamAsync();

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var requestContent = await streamTask.DefaultTimeout();
            var requestMessage = await requestContent.ReadStreamedMessageAsync(NullLogger.Instance, TestHelpers.ServiceMethod.RequestMarshaller.Deserializer, CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("1", requestMessage.Name);
            requestMessage = await requestContent.ReadStreamedMessageAsync(NullLogger.Instance, TestHelpers.ServiceMethod.RequestMarshaller.Deserializer, CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("2", requestMessage.Name);

            var responseMessage = await responseTask.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);
        }

        [Test]
        public void ClientStreamWriter_WriteWhilePendingWrite_ErrorThrown()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var streamContent = new StreamContent(new SyncPointMemoryStream());
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            var writeTask1 = call.RequestStream.WriteAsync(new HelloRequest { Name = "1" });
            Assert.IsFalse(writeTask1.IsCompleted);

            var writeTask2 = call.RequestStream.WriteAsync(new HelloRequest { Name = "2" });
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => writeTask2.DefaultTimeout());

            Assert.AreEqual("Cannot write message because the previous write is in progress.", ex.Message);
        }

        [Test]
        public void ClientStreamWriter_CompleteWhilePendingWrite_ErrorThrown()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var streamContent = new StreamContent(new SyncPointMemoryStream());
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            var writeTask1 = call.RequestStream.WriteAsync(new HelloRequest { Name = "1" });
            Assert.IsFalse(writeTask1.IsCompleted);

            var completeTask = call.RequestStream.CompleteAsync();
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => completeTask.DefaultTimeout());

            Assert.AreEqual("Cannot complete client stream writer because the previous write is in progress.", ex.Message);
        }

        [Test]
        public async Task ClientStreamWriter_WriteWhileComplete_ErrorThrown()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var streamContent = new StreamContent(new SyncPointMemoryStream());
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());
            await call.RequestStream.CompleteAsync();

            // Assert
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout());

            Assert.AreEqual("Cannot write message because the client stream writer is complete.", ex.Message);
        }

        [Test]
        public void ClientStreamWriter_WriteWithInvalidHttpStatus_ErrorThrown()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var streamContent = new StreamContent(new SyncPointMemoryStream());
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NotFound, streamContent));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout());

            Assert.AreEqual("Bad gRPC response. Expected HTTP status code 200. Got status code: 404", ex.Message);
        }

        [Test]
        public void ClientStreamWriter_WriteAfterResponseHasFinished_ErrorThrown()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            var ex = Assert.ThrowsAsync<RpcException>(async () => await call.RequestStream.WriteAsync(new HelloRequest()).DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.Status.StatusCode);
        }
    }
}
