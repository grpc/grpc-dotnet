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
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.NetCore.HttpClient.Internal;
using Grpc.NetCore.HttpClient.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.NetCore.HttpClient.Tests
{
    [TestFixture]
    public class AsyncServerStreamingCallTests
    {
        [Test]
        public async Task AsyncServerStreamingCall_NoContent_NoMessagesReturned()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>())));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            var responseStream = call.ResponseStream;

            // Assert
            Assert.IsNull(responseStream.Current);
            Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsNull(responseStream.Current);
        }

        [Test]
        public async Task AsyncServerStreamingCall_MessagesReturnedTogether_MessagesReceived()
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

            // Assert
            Assert.IsNull(responseStream.Current);

            Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsNotNull(responseStream.Current);
            Assert.AreEqual("Hello world 1", responseStream.Current.Message);

            Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsNotNull(responseStream.Current);
            Assert.AreEqual("Hello world 2", responseStream.Current.Message);

            Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        }

        [Test]
        public async Task AsyncServerStreamingCall_MessagesStreamed_MessagesReceived()
        {
            // Arrange
            var streamContent = new SyncPointMemoryStream();

            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            var responseStream = call.ResponseStream;

            // Assert
            Assert.IsNull(responseStream.Current);

            var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
            Assert.IsFalse(moveNextTask1.IsCompleted);

            await streamContent.AddDataAndWait(await TestHelpers.GetResponseDataAsync(new HelloReply
            {
                Message = "Hello world 1"
            }).DefaultTimeout()).DefaultTimeout();

            Assert.IsTrue(await moveNextTask1.DefaultTimeout());
            Assert.IsNotNull(responseStream.Current);
            Assert.AreEqual("Hello world 1", responseStream.Current.Message);

            var moveNextTask2 = responseStream.MoveNext(CancellationToken.None);
            Assert.IsFalse(moveNextTask2.IsCompleted);

            await streamContent.AddDataAndWait(await TestHelpers.GetResponseDataAsync(new HelloReply
            {
                Message = "Hello world 2"
            }).DefaultTimeout()).DefaultTimeout();

            Assert.IsTrue(await moveNextTask2.DefaultTimeout());
            Assert.IsNotNull(responseStream.Current);
            Assert.AreEqual("Hello world 2", responseStream.Current.Message);

            var moveNextTask3 = responseStream.MoveNext(CancellationToken.None);
            Assert.IsFalse(moveNextTask3.IsCompleted);

            await streamContent.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            Assert.IsFalse(await moveNextTask3.DefaultTimeout());

            var moveNextTask4 = responseStream.MoveNext(CancellationToken.None);
            Assert.IsTrue(moveNextTask4.IsCompleted);
            Assert.IsFalse(await moveNextTask3.DefaultTimeout());
        }

        [Test]
        public void ClientStreamReader_WriteWithInvalidHttpStatus_ErrorThrown()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var streamContent = new StreamContent(new SyncPointMemoryStream());
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NotFound, streamContent));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());

            Assert.AreEqual("Bad gRPC response. Expected HTTP status code 200. Got status code: 404", ex.Message);
        }

        [Test]
        public async Task AsyncServerStreamingCall_TrailersOnly_TrailersReturnedWithHeaders()
        {
            // Arrange
            HttpResponseMessage? responseMessage = null;
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()), grpcStatusCode: null);
                responseMessage.Headers.Add(GrpcProtocolConstants.StatusTrailer, StatusCode.OK.ToString("D"));
                responseMessage.Headers.Add(GrpcProtocolConstants.MessageTrailer, "Detail!");
                return Task.FromResult(responseMessage);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var headers = await call.ResponseHeadersAsync;
            await call.ResponseStream.MoveNext(CancellationToken.None);

            // Assert
            Assert.NotNull(responseMessage);
            Assert.IsFalse(responseMessage!.TrailingHeaders.Any()); // sanity check that there are no trailers

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
            Assert.AreEqual("Detail!", call.GetStatus().Detail);

            Assert.AreEqual(0, headers.Count);
            Assert.AreEqual(0, call.GetTrailers().Count);
        }

        [Test]
        public async Task AsyncServerStreamingCall_StatusInFooterAndMessageInHeader_IgnoreMessage()
        {
            // Arrange
            HttpResponseMessage? responseMessage = null;
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()));
                responseMessage.Headers.Add(GrpcProtocolConstants.MessageTrailer, "Ignored detail!");
                return Task.FromResult(responseMessage);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var headers = await call.ResponseHeadersAsync;
            await call.ResponseStream.MoveNext(CancellationToken.None);

            // Assert
            Assert.IsTrue(responseMessage!.TrailingHeaders.TryGetValues(GrpcProtocolConstants.StatusTrailer, out _)); // sanity status is in trailers

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
            Assert.AreEqual(null, call.GetStatus().Detail);

            Assert.AreEqual(0, headers.Count);
            Assert.AreEqual(0, call.GetTrailers().Count);
        }
    }
}
