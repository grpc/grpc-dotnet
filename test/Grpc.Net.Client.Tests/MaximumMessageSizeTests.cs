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
    public class MaximumMessageSizeTests
    {
        private async Task<HttpResponseMessage> HandleRequest(HttpRequestMessage request)
        {
            var requestStream = await request.Content.ReadAsStreamAsync();

            var helloRequest = await StreamExtensions.ReadSingleMessageAsync(
                requestStream,
                NullLogger.Instance,
                ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                "gzip",
                maximumMessageSize: null,
                GrpcProtocolConstants.DefaultCompressionProviders,
                CancellationToken.None);

            var reply = new HelloReply
            {
                Message = "Hello " + helloRequest!.Name
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        }

        [Test]
        public async Task AsyncUnaryCall_MessageSmallerThanSendMaxMessageSize_Success()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.SendMaxMessageSize = 100);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = "World"
            });

            // Assert
            var response = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello World", response.Message);
        }

        [Test]
        public async Task AsyncUnaryCall_MessageLargerThanSendMaxMessageSize_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.SendMaxMessageSize = 1);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = "World"
            });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_MessageLargerThanDefaultReceiveMaxMessageSize_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = new string('!', ChannelBuilder.DefaultReceiveMaxMessageSize + 1) // max size + 1 B
            });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_RemoveReceiveMaxMessageSize_Success()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ReceiveMaxMessageSize = null);
            var largeName = new string('!', ChannelBuilder.DefaultReceiveMaxMessageSize + 1); // 4 MB + 1 B

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = largeName
            });

            // Assert
            var response = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello " + largeName, response.Message);
        }

        [Test]
        public async Task AsyncUnaryCall_MessageSmallerThanReceiveMaxMessageSize_Success()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ReceiveMaxMessageSize = 100);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = "World"
            });

            // Assert
            var response = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello World", response.Message);
        }

        [Test]
        public async Task AsyncUnaryCall_MessageLargerThanReceiveMaxMessageSize_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ReceiveMaxMessageSize = 1);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = "World"
            });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_MessageSmallerThanSendMaxMessageSize_Success()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.SendMaxMessageSize = 100);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            await call.RequestStream.WriteAsync(new HelloRequest
            {
                Name = "World"
            });
            await call.RequestStream.CompleteAsync();

            // Assert
            await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("Hello World", call.ResponseStream.Current.Message);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_MessageLargerThanSendMaxMessageSize_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.SendMaxMessageSize = 1);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest
            {
                Name = "World"
            }));
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_MessageSmallerThanReceiveMaxMessageSize_Success()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ReceiveMaxMessageSize = 100);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            await call.RequestStream.WriteAsync(new HelloRequest
            {
                Name = "World"
            });
            await call.RequestStream.CompleteAsync();

            // Assert
            await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("Hello World", call.ResponseStream.Current.Message);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_MessageLargerThanReceiveMaxMessageSize_ThrowsError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ReceiveMaxMessageSize = 1);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            await call.RequestStream.WriteAsync(new HelloRequest
            {
                Name = "World"
            });
            await call.RequestStream.CompleteAsync();

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Status.Detail);
        }
    }
}
