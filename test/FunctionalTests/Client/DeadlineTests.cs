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
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;
using Streaming;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class DeadlineTests : FunctionalTestBase
    {
        [Test]
        public async Task Unary_SmallDeadline_ExceededWithoutReschedule()
        {
            var tcs = new TaskCompletionSource<DataMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<DataMessage> UnaryTimeout(DataMessage request, ServerCallContext context)
            {
                return tcs.Task;
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryTimeout);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(200)));

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);

            Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));

            tcs.SetResult(new DataMessage());
        }

        [Test]
        public async Task Unary_ServerResetCancellationStatus_DeadlineStatus()
        {
            TaskCompletionSource<object?> tcs = null!;
            async Task<DataMessage> UnaryTimeout(DataMessage request, ServerCallContext context)
            {
                var httpContext = context.GetHttpContext();
                var resetFeature = httpContext.Features.Get<IHttpResetFeature>()!;

                await tcs.Task;

                // Reset needs to arrive in client after it has exceeded deadline.
                // Delay can be imprecise. Wait extra time to ensure client has exceeded deadline.
                await Task.Delay(50);

                var cancelErrorCode = (httpContext.Request.Protocol == "HTTP/2") ? 0x8 : 0x10c;
                resetFeature.Reset(cancelErrorCode);

                return new DataMessage();
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryTimeout);

            var channel = CreateChannel();
            channel.DisableClientDeadline = true;

            var client = TestClientFactory.Create(channel, method);
            var deadline = TimeSpan.FromMilliseconds(300);

            for (var i = 0; i < 5; i++)
            {
                tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Act
                var headers = new Metadata
                {
                    { "remove-deadline", "true" }
                };
                var call = client.UnaryCall(new DataMessage(), new CallOptions(headers: headers, deadline: DateTime.UtcNow.Add(deadline)));

                await Task.Delay(deadline);
                tcs.SetResult(null);

                // Assert
                var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
                Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
                Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            }
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedDeadlineWithActiveCalls_Failure()
        {
            TaskCompletionSource<object?> tcs = null!;
            async Task ServerStreamingTimeout(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                var httpContext = context.GetHttpContext();
                var resetFeature = httpContext.Features.Get<IHttpResetFeature>()!;

                await tcs.Task;

                // Reset needs to arrive in client after it has exceeded deadline.
                // Delay can be imprecise. Wait extra time to ensure client has exceeded deadline.
                await Task.Delay(50);

                var cancelErrorCode = (httpContext.Request.Protocol == "HTTP/2") ? 0x8 : 0x10c;
                resetFeature.Reset(cancelErrorCode);
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingTimeout);

            var channel = CreateChannel();
            channel.DisableClientDeadline = true;

            var client = TestClientFactory.Create(channel, method);
            var deadline = TimeSpan.FromMilliseconds(300);

            for (var i = 0; i < 5; i++)
            {
                tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Act
                var headers = new Metadata
                {
                    { "remove-deadline", "true" }
                };
                var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(headers: headers, deadline: DateTime.UtcNow.Add(deadline)));

                await Task.Delay(deadline);
                tcs.SetResult(null);

                // Assert
                var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
                Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
                Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            }
        }

        [Test]
        public async Task Unary_DeadlineInBetweenReadAsyncCalls_DeadlineExceededStatus()
        {
            Task<DataMessage> Unary(DataMessage request, ServerCallContext context)
            {
                return Task.FromResult(new DataMessage());
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(Unary);

            var http = Fixture.CreateHandler(TestServerEndpointName.Http2);

            var channel = GrpcChannel.ForAddress(http.address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = new PauseHttpHandler { InnerHandler = http.handler }
            });

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(200)));

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        private class PauseHttpHandler : DelegatingHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = await base.SendAsync(request, cancellationToken);

                var newHttpContent = new PauseHttpContent(response.Content);
                newHttpContent.Headers.ContentType = response.Content.Headers.ContentType;

                response.Content = newHttpContent;

                return response;
            }

            private class PauseHttpContent : HttpContent
            {
                private readonly HttpContent _inner;
                private Stream? _innerStream;

                public PauseHttpContent(HttpContent inner)
                {
                    _inner = inner;
                }

                protected override async Task<Stream> CreateContentReadStreamAsync()
                {
                    var stream = await _inner.ReadAsStreamAsync().ConfigureAwait(false);

                    return new PauseStream(stream);
                }

                protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                {
                    _innerStream = await _inner.ReadAsStreamAsync().ConfigureAwait(false);

                    _innerStream = new PauseStream(_innerStream);

                    await _innerStream.CopyToAsync(stream).ConfigureAwait(false);
                }

                protected override bool TryComputeLength(out long length)
                {
                    length = 0;
                    return false;
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing)
                    {
                        // This is important. Disposing original response content will cancel the gRPC call.
                        _inner.Dispose();
                        _innerStream?.Dispose();
                    }

                    base.Dispose(disposing);
                }

                private class PauseStream : Stream
                {
                    private Stream _stream;

                    public PauseStream(Stream stream)
                    {
                        _stream = stream;
                    }

                    public override bool CanRead => _stream.CanRead;
                    public override bool CanSeek => _stream.CanSeek;
                    public override bool CanWrite => _stream.CanWrite;
                    public override long Length => _stream.Length;
                    public override long Position
                    {
                        get => _stream.Position;
                        set => _stream.Position = value;
                    }

                    public override void Flush()
                    {
                        _stream.Flush();
                    }

                    public override int Read(byte[] buffer, int offset, int count)
                    {
                        return _stream.Read(buffer, offset, count);
                    }

                    public override long Seek(long offset, SeekOrigin origin)
                    {
                        return _stream.Seek(offset, origin);
                    }

                    public override void SetLength(long value)
                    {
                        _stream.SetLength(value);
                    }

                    public override void Write(byte[] buffer, int offset, int count)
                    {
                        _stream.Write(buffer, offset, count);
                    }

                    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                    {
                        // Wait for call to be canceled.
                        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                        cancellationToken.Register(() => tcs.SetResult(null));
                        await tcs.Task;

                        // Wait a little longer to give time for HttpResponseMessage dispose to complete.
                        await Task.Delay(50);

                        // Still try to read data from canceled request.
                        return await _stream.ReadAsync(buffer, cancellationToken);
                    }
                }
            }
        }
    }
}
