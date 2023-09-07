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
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class AsyncServerStreamingCallTests
{
    [Test]
    public async Task AsyncServerStreamingCall_NoContent_NoMessagesReturned()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>())));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

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
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponsesContent(
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
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

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

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);

        var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask1.IsCompleted);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 1"
        }).DefaultTimeout()).DefaultTimeout();

        Assert.IsTrue(await moveNextTask1.DefaultTimeout());
        Assert.IsNotNull(responseStream.Current);
        Assert.AreEqual("Hello world 1", responseStream.Current.Message);

        var moveNextTask2 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask2.IsCompleted);

        // Current is cleared after MoveNext is called.
        Assert.IsNull(responseStream.Current);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 2"
        }).DefaultTimeout()).DefaultTimeout();

        Assert.IsTrue(await moveNextTask2.DefaultTimeout());
        Assert.IsNotNull(responseStream.Current);
        Assert.AreEqual("Hello world 2", responseStream.Current.Message);

        var moveNextTask3 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask3.IsCompleted);

        await streamContent.EndStreamAndWait().DefaultTimeout();

        Assert.IsFalse(await moveNextTask3.DefaultTimeout());

        var moveNextTask4 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsTrue(moveNextTask4.IsCompleted);
        Assert.IsFalse(await moveNextTask3.DefaultTimeout());
    }

    [Test]
    public async Task AsyncServerStreamingCall_MessagesStreamedThenError_ErrorStatus()
    {
        // Arrange
        var streamContent = new SyncPointMemoryStream();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);

        var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask1.IsCompleted);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 1"
        }).DefaultTimeout()).DefaultTimeout();

        Assert.IsTrue(await moveNextTask1.DefaultTimeout());
        Assert.IsNotNull(responseStream.Current);
        Assert.AreEqual("Hello world 1", responseStream.Current.Message);

        var moveNextTask2 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask2.IsCompleted);

        await streamContent.AddExceptionAndWait(new Exception("Exception!")).DefaultTimeout();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask2).DefaultTimeout();
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
        Assert.AreEqual("Error reading next message. Exception: Exception!", call.GetStatus().Detail);
    }

    [Test]
    public async Task AsyncServerStreamingCall_MessagesStreamedThenCancellation_ErrorStatus()
    {
        // Arrange
        var streamContent = new SyncPointMemoryStream();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);

        var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask1.IsCompleted);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 1"
        }).DefaultTimeout()).DefaultTimeout();

        Assert.IsTrue(await moveNextTask1.DefaultTimeout());
        Assert.IsNotNull(responseStream.Current);
        Assert.AreEqual("Hello world 1", responseStream.Current.Message);

        var cts = new CancellationTokenSource();

        var moveNextTask2 = responseStream.MoveNext(cts.Token);
        Assert.IsFalse(moveNextTask2.IsCompleted);

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask2).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("Call canceled by the client.", call.GetStatus().Detail);
    }

    [Test]
    public async Task AsyncServerStreamingCall_MessagesStreamedThenDispose_ErrorStatus()
    {
        // Arrange
        var streamContent = new SyncPointMemoryStream();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);

        var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask1.IsCompleted);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 1"
        }).DefaultTimeout()).DefaultTimeout();

        Assert.IsTrue(await moveNextTask1.DefaultTimeout());
        Assert.IsNotNull(responseStream.Current);
        Assert.AreEqual("Hello world 1", responseStream.Current.Message);

        var moveNextTask2 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask2.IsCompleted);

        call.Dispose();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask2).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("gRPC call disposed.", call.GetStatus().Detail);
    }

    [Test]
    public async Task AsyncServerStreamingCall_DisposeDuringPendingRead_NoReadMessageError()
    {
        // Arrange
        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);

        var streamContent = new SyncPointMemoryStream();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: loggerFactory);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);

        var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask1.IsCompleted);

        call.Dispose();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask1).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("gRPC call disposed.", call.GetStatus().Detail);

        Assert.IsFalse(testSink.Writes.Any(w => w.EventId.Name == "ErrorReadingMessage"), "ErrorReadingMessage shouldn't be logged on dispose.");
    }

    [Test]
    public async Task ClientStreamReader_WriteWithInvalidHttpStatus_ErrorThrown()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NotFound));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).DefaultTimeout();

        Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
        Assert.AreEqual("Bad gRPC response. HTTP status code: 404", ex.Status.Detail);
    }

    [Test]
    public async Task AsyncServerStreamingCall_TrailersOnly_TrailersReturnedWithHeaders()
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
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var headers = await call.ResponseHeadersAsync.DefaultTimeout();
        Assert.IsFalse(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());

        // Assert
        Assert.NotNull(responseMessage);

        Assert.IsFalse(responseMessage!.TrailingHeaders().Any()); // sanity check that there are no trailers

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
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            responseMessage = ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()));
            responseMessage.Headers.Add(GrpcProtocolConstants.MessageTrailer, "Ignored detail!");
            return Task.FromResult(responseMessage);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var headers = await call.ResponseHeadersAsync.DefaultTimeout();
        await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout();

        // Assert
        Assert.IsTrue(responseMessage!.TrailingHeaders().TryGetValues(GrpcProtocolConstants.StatusTrailer, out _)); // sanity status is in trailers

        Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        Assert.AreEqual(string.Empty, call.GetStatus().Detail);

        Assert.AreEqual(0, headers.Count);
        Assert.AreEqual(0, call.GetTrailers().Count);
    }
}
