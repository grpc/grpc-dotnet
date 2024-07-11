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
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class HttpContentClientStreamReaderTests
{
    [Test]
    public async Task MoveNext_TokenCanceledBeforeCall_ThrowError()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var stream = new SyncPointMemoryStream();
            var content = new StreamContent(stream);
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
        });

        var testSink = new TestSink(e => e.LogLevel >= LogLevel.Error);
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var channel = CreateChannel(httpClient, loggerFactory: testLoggerFactory);
        var call = CreateGrpcCall(channel);
        call.StartServerStreaming(new HelloRequest());

        // Act
        var moveNextTask = call.ClientStreamReader!.MoveNext(cts.Token);

        // Assert
        Assert.IsTrue(moveNextTask.IsCompleted);
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask).DefaultTimeout();

        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        Assert.AreEqual(0, testSink.Writes.Count);
    }

    [Test]
    public async Task MoveNext_TokenCanceledBeforeCall_ThrowOperationCanceledExceptionOnCancellation_ThrowError()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var stream = new SyncPointMemoryStream();
            var content = new StreamContent(stream);
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
        });

        var testSink = new TestSink(e => e.LogLevel >= LogLevel.Error);
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var channel = CreateChannel(httpClient, loggerFactory: testLoggerFactory, throwOperationCanceledOnCancellation: true);
        var call = CreateGrpcCall(channel);
        call.StartServerStreaming(new HelloRequest());

        // Act
        var moveNextTask = call.ClientStreamReader!.MoveNext(cts.Token);

        // Assert
        Assert.IsTrue(moveNextTask.IsCompleted);
        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => moveNextTask).DefaultTimeout();

        Assert.AreEqual(0, testSink.Writes.Count);
    }

    [Test]
    public async Task MoveNext_TokenCanceledDuringCall_ThrowError()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var stream = new SyncPointMemoryStream();
            var content = new StreamContent(stream);
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
        });

        var testSink = new TestSink(e => e.LogLevel >= LogLevel.Error);
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var channel = CreateChannel(httpClient, loggerFactory: testLoggerFactory);
        var call = CreateGrpcCall(channel);
        call.StartServerStreaming(new HelloRequest());

        // Act
        var moveNextTask = call.ClientStreamReader!.MoveNext(cts.Token);

        // Assert
        Assert.IsFalse(moveNextTask.IsCompleted);

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask).DefaultTimeout();

        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        Assert.AreEqual(0, testSink.Writes.Count);
    }

    [Test]
    public async Task MoveNext_TokenCanceledDuringCall_ThrowOperationCanceledOnCancellation_ThrowError()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var stream = new SyncPointMemoryStream();
            var content = new StreamContent(stream);
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
        });

        var testSink = new TestSink(e => e.LogLevel >= LogLevel.Error);
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var channel = CreateChannel(httpClient, loggerFactory: testLoggerFactory, throwOperationCanceledOnCancellation: true);
        var call = CreateGrpcCall(channel);
        call.StartServerStreaming(new HelloRequest());

        // Act
        var moveNextTask = call.ClientStreamReader!.MoveNext(cts.Token);

        // Assert
        Assert.IsFalse(moveNextTask.IsCompleted);

        cts.Cancel();

        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => moveNextTask).DefaultTimeout();

        Assert.AreEqual(0, testSink.Writes.Count);
    }

    [Test]
    public async Task MoveNext_MultipleCallsWithoutAwait_ThrowError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var stream = new SyncPointMemoryStream();
            var content = new StreamContent(stream);
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
        });

        var testSink = new TestSink(e => e.LogLevel >= LogLevel.Error);
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var channel = CreateChannel(httpClient, loggerFactory: testLoggerFactory);
        var call = CreateGrpcCall(channel);
        call.StartServerStreaming(new HelloRequest());

        // Act
        var moveNextTask1 = call.ClientStreamReader!.MoveNext(CancellationToken.None);
        var moveNextTask2 = call.ClientStreamReader.MoveNext(CancellationToken.None);

        // Assert
        Assert.IsFalse(moveNextTask1.IsCompleted);

        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => moveNextTask2).DefaultTimeout();
        Assert.AreEqual("Can't read the next message because the previous read is still in progress.", ex.Message);

        Assert.AreEqual(1, testSink.Writes.Count);
        var write = testSink.Writes.ElementAt(0);
        Assert.AreEqual("ReadMessageError", write.EventId.Name);
        Assert.AreEqual(ex, write.Exception);
    }

    [Test]
    public async Task MoveNext_StreamThrowsIOException_ThrowErrorUnavailable()
    {
        // Arrange
        var handler = TestHttpMessageHandler.Create(request =>
        {
            var stream = new TestStream();
            var content = new StreamContent(stream);
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
        });

        var testSink = new TestSink(e => e.LogLevel >= LogLevel.Error);
        var testLoggerFactory = new TestLoggerFactory(testSink, enabled: true);

        var channel = CreateChannel(handler, "http://localhost", loggerFactory: testLoggerFactory);
        var call = CreateGrpcCall(channel);
        call.StartServerStreaming(new HelloRequest());

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ClientStreamReader!.MoveNext(CancellationToken.None)).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual("Error reading next message. IOException: Test", ex.Status.Detail);
    }

    private static GrpcCall<HelloRequest, HelloReply> CreateGrpcCall(GrpcChannel channel)
    {
        var uri = new Uri("http://localhost");

        return new GrpcCall<HelloRequest, HelloReply>(
            ClientTestHelpers.ServiceMethod,
            new GrpcMethodInfo(new GrpcCallScope(ClientTestHelpers.ServiceMethod.Type, uri), uri, methodConfig: null),
            new CallOptions(),
            channel,
            attemptCount: 0,
            forceAsyncHttpResponse: false);
    }

    private static GrpcChannel CreateChannel(HttpClient httpClient, ILoggerFactory? loggerFactory = null, bool? throwOperationCanceledOnCancellation = null)
    {
        return GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = loggerFactory,
                ThrowOperationCanceledOnCancellation = throwOperationCanceledOnCancellation ?? false
            });
    }

    private static GrpcChannel CreateChannel(HttpMessageHandler httpClient, string address, ILoggerFactory? loggerFactory = null, bool? throwOperationCanceledOnCancellation = null)
    {
        return GrpcChannel.ForAddress(
            address,
            new GrpcChannelOptions
            {
                HttpHandler = httpClient,
                LoggerFactory = loggerFactory,
                ThrowOperationCanceledOnCancellation = throwOperationCanceledOnCancellation ?? false
            });
    }

    private class TestStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek { get; }
        public override bool CanWrite { get; }
        public override long Length { get; }
        public override long Position { get; set; }

        public override void Flush()
        {
            throw new NotImplementedException();
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

#if !NET462
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(Task.FromException<int>(new IOException("Test")));
        }
#else
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromException<int>(new IOException("Test"));
        }
#endif
    }
}
