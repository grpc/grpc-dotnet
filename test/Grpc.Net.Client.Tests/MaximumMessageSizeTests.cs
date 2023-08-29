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

using System.Net;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class MaximumMessageSizeTests
{
    private async Task<HttpResponseMessage> HandleRequest(HttpRequestMessage request)
    {
        var requestStream = await request.Content!.ReadAsStreamAsync().DefaultTimeout();

        var helloRequest = await StreamSerializationHelper.ReadMessageAsync(
            requestStream,
            ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
            "gzip",
            maximumMessageSize: null,
            GrpcProtocolConstants.DefaultCompressionProviders,
            singleMessage: true,
            CancellationToken.None).DefaultTimeout();

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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxSendMessageSize = 100);

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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxSendMessageSize = 1);

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
            Name = new string('!', GrpcChannel.DefaultMaxReceiveMessageSize + 1) // max size + 1 B
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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxReceiveMessageSize = null);
        var largeName = new string('!', GrpcChannel.DefaultMaxReceiveMessageSize + 1); // max size + 1 B

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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxReceiveMessageSize = 100);

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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxReceiveMessageSize = 1);

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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxSendMessageSize = 100);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();
        await call.RequestStream.WriteAsync(new HelloRequest
        {
            Name = "World"
        });
        await call.RequestStream.CompleteAsync().DefaultTimeout();

        // Assert
        await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout();
        Assert.AreEqual("Hello World", call.ResponseStream.Current.Message);
    }

    [Test]
    public async Task AsyncDuplexStreamingCall_MessageLargerThanSendMaxMessageSize_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxSendMessageSize = 1);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();

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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxReceiveMessageSize = 100);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();
        await call.RequestStream.WriteAsync(new HelloRequest
        {
            Name = "World"
        });
        await call.RequestStream.CompleteAsync().DefaultTimeout();

        // Assert
        await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout();
        Assert.AreEqual("Hello World", call.ResponseStream.Current.Message);
    }

    [Test]
    public async Task AsyncDuplexStreamingCall_MessageLargerThanReceiveMaxMessageSize_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(HandleRequest);
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.MaxReceiveMessageSize = 1);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();
        await call.RequestStream.WriteAsync(new HelloRequest
        {
            Name = "World"
        });
        await call.RequestStream.CompleteAsync().DefaultTimeout();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None));
        Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
        Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Status.Detail);
    }
}
