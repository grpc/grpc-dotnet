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
using System.Net.Http.Headers;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class AsyncClientStreamingCallTests
{
    [Test]
    public async Task AsyncClientStreamingCall_Success_HttpRequestMessagePopulated()
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
        var call = invoker.AsyncClientStreamingCall();

        await call.RequestStream.CompleteAsync().DefaultTimeout();

        var response = await call;

        // Assert
        Assert.AreEqual("Hello world", response.Message);

        Assert.IsNotNull(httpRequestMessage);
        Assert.AreEqual(new Version(2, 0), httpRequestMessage!.Version);
        Assert.AreEqual(HttpMethod.Post, httpRequestMessage.Method);
        Assert.AreEqual(new Uri("https://localhost/ServiceName/MethodName"), httpRequestMessage.RequestUri);
        Assert.AreEqual(new MediaTypeHeaderValue("application/grpc"), httpRequestMessage.Content?.Headers?.ContentType);
    }

    [Test]
    public async Task AsyncClientStreamingCall_Success_RequestContentSent()
    {
        // Arrange
        var requestContentTcs = new TaskCompletionSource<Task<Stream>>(TaskCreationOptions.RunContinuationsAsynchronously);

        PushStreamContent<HelloRequest, HelloReply>? content = null;

        var handler = TestHttpMessageHandler.Create(async request =>
        {
            content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
            var streamTask = content.ReadAsStreamAsync();
            requestContentTcs.SetResult(streamTask);

            // Wait for RequestStream.CompleteAsync()
            await streamTask;

            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(handler, "http://localhost");

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var requestContentTask = await requestContentTcs.Task.DefaultTimeout();

        // Assert
        Assert.IsNotNull(call);
        Assert.IsNotNull(content);

        var responseTask = call.ResponseAsync;
        Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

        await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
        await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

        await call.RequestStream.CompleteAsync().DefaultTimeout();

        var requestContent = await requestContentTask.DefaultTimeout();
        var requestMessage = await StreamSerializationHelper.ReadMessageAsync(
            requestContent,
            ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
            GrpcProtocolConstants.IdentityGrpcEncoding,
            maximumMessageSize: null,
            GrpcProtocolConstants.DefaultCompressionProviders,
            singleMessage: false,
            CancellationToken.None).DefaultTimeout();
        Assert.AreEqual("1", requestMessage!.Name);
        requestMessage = await StreamSerializationHelper.ReadMessageAsync(
            requestContent,
            ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
            GrpcProtocolConstants.IdentityGrpcEncoding,
            maximumMessageSize: null,
            GrpcProtocolConstants.DefaultCompressionProviders,
            singleMessage: false,
            CancellationToken.None).DefaultTimeout();
        Assert.AreEqual("2", requestMessage!.Name);

        var responseMessage = await responseTask.DefaultTimeout();
        Assert.AreEqual("Hello world", responseMessage.Message);
    }

    [Test]
    public async Task ClientStreamWriter_WriteWhilePendingWrite_ErrorThrown()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var streamContent = new StreamContent(new SyncPointMemoryStream());
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();

        // Assert
        var writeTask1 = call.RequestStream.WriteAsync(new HelloRequest { Name = "1" });
        Assert.IsFalse(writeTask1.IsCompleted);

        var writeTask2 = call.RequestStream.WriteAsync(new HelloRequest { Name = "2" });
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => writeTask2).DefaultTimeout();

        Assert.AreEqual("Can't write the message because the previous write is in progress.", ex.Message);
    }

    [Test]
    public async Task ClientStreamWriter_DisposeWhilePendingWrite_NoReadMessageError()
    {
        // Arrange
        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);
        PushStreamContent<HelloRequest, HelloReply>? content = null;

        var responseTcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
            return responseTcs.Task;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: loggerFactory);

        // Act
        var call = invoker.AsyncClientStreamingCall();

        // Assert
        var writeTask1 = call.RequestStream.WriteAsync(new HelloRequest { Name = "1" });

        var writeSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testStream = new TestStream(writeSyncPoint);
        var serializeToStreamTask = content!.SerializeToStreamAsync(testStream);

        Assert.IsFalse(writeTask1.IsCompleted);
        await writeSyncPoint.WaitForSyncPoint().DefaultTimeout();

        call.Dispose();
        writeSyncPoint.Continue();

        var ex1 = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex1.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("gRPC call disposed.", call.GetStatus().Detail);

        var ex2 = await ExceptionAssert.ThrowsAsync<RpcException>(() => writeTask1).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex2.StatusCode);

        Assert.IsFalse(testSink.Writes.Any(w => w.EventId.Name == "ErrorSendingMessage"), "ErrorSendingMessage shouldn't be logged on dispose.");
    }

    [Test]
    public async Task ClientStreamWriter_CompleteWhilePendingWrite_ErrorThrown()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var streamContent = new StreamContent(new SyncPointMemoryStream());
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();

        // Assert
        var writeTask1 = call.RequestStream.WriteAsync(new HelloRequest { Name = "1" });
        Assert.IsFalse(writeTask1.IsCompleted);

        var completeTask = call.RequestStream.CompleteAsync().DefaultTimeout();
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => completeTask).DefaultTimeout();

        Assert.AreEqual("Can't complete the client stream writer because the previous write is in progress.", ex.Message);
    }

    [Test]
    public async Task ClientStreamWriter_WriteWhileComplete_ErrorThrown()
    {
        // Arrange
        var streamContent = new SyncPointMemoryStream();
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        await call.RequestStream.CompleteAsync().DefaultTimeout();
        var resultTask = call.ResponseAsync;

        // Assert
        var writeException1 = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
        Assert.AreEqual("Request stream has already been completed.", writeException1.Message);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 1"
        }).DefaultTimeout()).DefaultTimeout();
        await streamContent.EndStreamAndWait();

        var result = await resultTask.DefaultTimeout();
        Assert.AreEqual("Hello world 1", result.Message);

        var writeException2 = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "2" })).DefaultTimeout();
        Assert.AreEqual("Request stream has already been completed.", writeException2.Message);
    }

    [Test]
    public async Task ClientStreamWriter_WriteWithInvalidHttpStatus_ErrorThrown()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var streamContent = new StreamContent(new SyncPointMemoryStream());
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.NotFound, streamContent));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var writeException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
        var resultException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual("Bad gRPC response. HTTP status code: 404", writeException.Status.Detail);
        Assert.AreEqual(StatusCode.Unimplemented, writeException.StatusCode);

        Assert.AreEqual("Bad gRPC response. HTTP status code: 404", resultException.Status.Detail);
        Assert.AreEqual(StatusCode.Unimplemented, resultException.StatusCode);
    }

    [Test]
    public async Task ClientStreamWriter_WriteAfterResponseHasFinished_ErrorThrown()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest())).DefaultTimeout();
        var result = await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.OK, ex.StatusCode);
        Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        Assert.AreEqual(string.Empty, call.GetStatus().Detail);

        Assert.AreEqual("Hello world", result.Message);
    }

    [Test]
    public async Task AsyncClientStreamingCall_ErrorWhileWriting_StatusExceptionThrown()
    {
        // Arrange
        PushStreamContent<HelloRequest, HelloReply>? content = null;

        var responseTcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
            return responseTcs.Task;
        });

        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act

        // Client starts call
        var call = invoker.AsyncClientStreamingCall();
        // Client starts request stream write
        var writeTask = call.RequestStream.WriteAsync(new HelloRequest());

        // Simulate HttpClient starting to accept the write. Stream.WriteAsync is blocked.
        var writeSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testStream = new TestStream(writeSyncPoint);
        var serializeToStreamTask = content!.SerializeToStreamAsync(testStream);

        // Server completes response.
        await writeSyncPoint.WaitForSyncPoint().DefaultTimeout();
        responseTcs.SetResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>()), grpcStatusCode: StatusCode.InvalidArgument));

        await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.InvalidArgument, call.GetStatus().StatusCode);

        // Unblock Stream.WriteAsync
        writeSyncPoint.Continue();

        // Get error thrown from write task. It should have the status returned by the server.
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => writeTask).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.AreEqual(StatusCode.InvalidArgument, call.GetStatus().StatusCode);
        Assert.AreEqual(string.Empty, call.GetStatus().Detail);
    }

    private sealed class TestStream : Stream
    {
        private readonly SyncPoint _writeSyncPoint;

        public TestStream(SyncPoint writeSyncPoint)
        {
            _writeSyncPoint = writeSyncPoint;
        }

        public override bool CanRead { get; }
        public override bool CanSeek { get; }
        public override bool CanWrite { get; }
        public override long Length { get; }
        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

#if !NET462_OR_GREATER
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await _writeSyncPoint.WaitToContinue();
            throw new OperationCanceledException();
        }
#else
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _writeSyncPoint.WaitToContinue();
            throw new OperationCanceledException();
        }
#endif
    }

    [Test]
    public async Task ClientStreamWriter_CancelledBeforeCallStarts_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: new CancellationToken(true)));

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest())).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ClientStreamWriter_CancelledBeforeCallStarts_ThrowOperationCanceledOnCancellation_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ThrowOperationCanceledOnCancellation = true);

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: new CancellationToken(true)));

        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.RequestStream.WriteAsync(new HelloRequest())).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ClientStreamWriter_CallThrowsException_WriteAsyncThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new InvalidOperationException("Error!"));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var writeException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest())).DefaultTimeout();
        var resultException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual("Error starting gRPC call. InvalidOperationException: Error!", writeException.Status.Detail);
        Assert.AreEqual("Error starting gRPC call. InvalidOperationException: Error!", resultException.Status.Detail);
        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
    }
}
