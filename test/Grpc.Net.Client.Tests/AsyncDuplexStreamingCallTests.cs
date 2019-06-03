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
    public class AsyncDuplexStreamingCallTests
    {
        [Test]
        public async Task AsyncDuplexStreamingCall_NoContent_NoMessagesReturned()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>())));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

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
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>())));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            var responseStream = call.ResponseStream;

            // Assert
            Assert.IsNull(responseStream.Current);
            Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsNull(responseStream.Current);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_MessagesStreamed_MessagesReceived()
        {
            // Arrange
            var streamContent = new SyncPointMemoryStream();

            PushStreamContent? content = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                content = (PushStreamContent)request.Content;
                await content.PushComplete.DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            var requestStream = call.RequestStream;
            var responseStream = call.ResponseStream;

            // Assert
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            Assert.IsNotNull(content);
            var requestContent = await content!.ReadAsStreamAsync().DefaultTimeout();
            var requestMessage = await requestContent.ReadStreamedMessageAsync(NullLogger.Instance, TestHelpers.ServiceMethod.RequestMarshaller.Deserializer, CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("1", requestMessage.Name);
            requestMessage = await requestContent.ReadStreamedMessageAsync(NullLogger.Instance, TestHelpers.ServiceMethod.RequestMarshaller.Deserializer, CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("2", requestMessage.Name);

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
    }
}
