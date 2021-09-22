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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Retry
{
    [TestFixture]
    public class HedgingTests
    {
        [TestCase(2)]
        [TestCase(10)]
        [TestCase(100)]
        public async Task AsyncUnaryCall_OneAttempt_Success(int maxAttempts)
        {
            // Arrange
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                await tcs.Task;

                await request.Content!.CopyToAsync(new MemoryStream());

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: maxAttempts);
            var invoker = HttpClientCallInvokerFactory.Create(
                httpClient,
                serviceConfig: serviceConfig,
                configure: o => o.MaxRetryAttempts = maxAttempts);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => callCount == maxAttempts, "All calls made at once.");
            tcs.SetResult(null);

            var rs = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello world", rs.Message);
            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_ManyParallelCalls_ReadDirectlyToRequestStream()
        {
            // Arrange
            var requestStreams = new List<WriterTestStream>();
            var attempts = 100;

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                WriterTestStream writerTestStream;
                lock (requestStreams)
                {
                    Interlocked.Increment(ref callCount);
                    writerTestStream = new WriterTestStream();
                    requestStreams.Add(writerTestStream);
                }
                await request.Content!.CopyToAsync(writerTestStream);

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: attempts);
            var invoker = HttpClientCallInvokerFactory.Create(
                httpClient,
                serviceConfig: serviceConfig,
                configure: o => o.MaxRetryAttempts = attempts);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions());
            var writeAsyncTask = call.RequestStream.WriteAsync(new HelloRequest { Name = "World" });

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => callCount == attempts, "All calls made at once.");

            var firstMessages = await Task.WhenAll(requestStreams.Select(s => s.WaitForDataAsync())).DefaultTimeout();
            await writeAsyncTask.DefaultTimeout();

            foreach (var message in firstMessages)
            {
                Assert.IsTrue(firstMessages[0].Span.SequenceEqual(message.Span));
            }

            writeAsyncTask = call.RequestStream.WriteAsync(new HelloRequest { Name = "World 2" });
            var secondMessages = await Task.WhenAll(requestStreams.Select(s => s.WaitForDataAsync())).DefaultTimeout();
            await writeAsyncTask.DefaultTimeout();

            foreach (var message in secondMessages)
            {
                Assert.IsTrue(secondMessages[0].Span.SequenceEqual(message.Span));
            }

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var rs = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello world", rs.Message);
            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        }

        private class WriterTestStream : Stream
        {
            public TaskCompletionSource<ReadOnlyMemory<byte>> WriteAsyncTcs = new TaskCompletionSource<ReadOnlyMemory<byte>>(TaskCreationOptions.RunContinuationsAsynchronously);

            public override bool CanRead { get; }
            public override bool CanSeek { get; }
            public override bool CanWrite { get; }
            public override long Length { get; }
            public override long Position { get; set; }

            private SyncPoint _syncPoint;
            private Func<Task> _awaiter;
            private ReadOnlyMemory<byte> _currentWriteData;

            public WriterTestStream()
            {
                _awaiter = SyncPoint.Create(out _syncPoint, runContinuationsAsynchronously: true);
            }

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

#if NET472
            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
#else
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
#endif
            {
#if NET472
                var data = buffer.AsMemory(offset, count);
#endif
                _currentWriteData = data.ToArray();

                await _awaiter();
                // Wait until data is read by WaitForDataAsync
                //await _syncPoint.WaitForSyncPoint();
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }

            public async Task<ReadOnlyMemory<byte>> WaitForDataAsync()
            {
                await _syncPoint.WaitForSyncPoint();

                ResetSyncPointAndContinuePrevious();

                //await _awaiter();
                return _currentWriteData;
            }

            private void ResetSyncPointAndContinuePrevious()
            {
                // We have read all data
                // Signal AddDataAndWait to continue
                // Reset sync point for next read
                var syncPoint = _syncPoint;

                ResetSyncPoint();

                syncPoint.Continue();
            }

            private void ResetSyncPoint()
            {
                _awaiter = SyncPoint.Create(out _syncPoint, runContinuationsAsynchronously: true);
            }
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedAttempts_Failure()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var requestMessages = new List<HelloRequest>();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                // All calls are in-progress at once.
                Interlocked.Increment(ref callCount);
                if (callCount == 5)
                {
                    tcs.TrySetResult(null);
                }
                await tcs.Task;

                var requestContent = await request.Content!.ReadAsStreamAsync();
                var requestMessage = await ReadRequestMessage(requestContent);
                lock (requestMessages)
                {
                    requestMessages.Add(requestMessage!);
                }

                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(5, callCount);
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

            Assert.AreEqual(5, requestMessages.Count);
            foreach (var requestMessage in requestMessages)
            {
                Assert.AreEqual("World", requestMessage.Name);
            }
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedDeadlineWithActiveCalls_Failure()
        {
            // Arrange
            var testSink = new TestSink();
            var services = new ServiceCollection();
            services.AddLogging(b =>
            {
                b.AddProvider(new TestLoggerProvider(testSink));
            });
            services.AddNUnitLogger();
            var provider = services.BuildServiceProvider();

            var tcs = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async (request, ct) =>
            {
                // Ensure SendAsync call doesn't hang upon cancellation by gRPC client.
                using var registration = ct.Register(() => tcs.TrySetCanceled());

                Interlocked.Increment(ref callCount);
                return await tcs.Task;
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(hedgingDelay: TimeSpan.FromMilliseconds(200));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: provider.GetRequiredService<ILoggerFactory>(), serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(100)), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(1, callCount);
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);

            var write = testSink.Writes.Single(w => w.EventId.Name == "CallCommited");
            Assert.AreEqual("Call commited. Reason: DeadlineExceeded", write.State.ToString());
        }

        [Test]
        public async Task AsyncUnaryCall_ManyAttemptsNoDelay_MarshallerCalledOnce()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);
                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            var marshallerCount = 0;
            var requestMarshaller = Marshallers.Create<HelloRequest>(
                r =>
                {
                    Interlocked.Increment(ref marshallerCount);
                    return r.ToByteArray();
                },
                data => HelloRequest.Parser.ParseFrom(data));
            var method = ClientTestHelpers.GetServiceMethod(requestMarshaller: requestMarshaller);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(method, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

            Assert.AreEqual(5, callCount);
            Assert.AreEqual(1, marshallerCount);
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedAttempts_PusbackDelay_Failure()
        {
            // Arrange
            var stopwatch = new Stopwatch();
            var callIntervals = new List<long>();
            var hedgeDelay = TimeSpan.FromMilliseconds(100);
            const int timerResolutionMs = 15 * 2; // Timer has a precision of about 15ms. Double it, just to be safe

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callIntervals.Add(stopwatch.ElapsedMilliseconds);
                stopwatch.Restart();
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: hedgeDelay.TotalMilliseconds.ToString());
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 2, hedgingDelay: hedgeDelay);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            stopwatch.Start();
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(2, callCount);
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

            // First call should happen immediately
            Assert.LessOrEqual(callIntervals[0], hedgeDelay.TotalMilliseconds);

            // Second call should happen after delay
            Assert.GreaterOrEqual(callIntervals[1], hedgeDelay.TotalMilliseconds - timerResolutionMs);
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedAttempts_NoPusbackDelay_Failure()
        {
            // Arrange
            var stopwatch = new Stopwatch();
            var callIntervals = new List<long>();
            var hedgeDelay = TimeSpan.FromSeconds(10);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callIntervals.Add(stopwatch.ElapsedMilliseconds);
                stopwatch.Restart();
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 2, hedgingDelay: hedgeDelay);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            stopwatch.Start();
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(2, callCount);
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

            // First call should happen immediately
            Assert.LessOrEqual(callIntervals[0], hedgeDelay.TotalMilliseconds);

            // Second call should happen immediately
            Assert.LessOrEqual(callIntervals[1], hedgeDelay.TotalMilliseconds);
        }

        [Test]
        public async Task AsyncUnaryCall_PushbackDelay_PushbackDelayUpdatesNextCallDelay()
        {
            // Arrange
            var stopwatch = new Stopwatch();
            var callIntervals = new List<long>();
            var hedgingDelay = TimeSpan.FromSeconds(10);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callIntervals.Add(stopwatch.ElapsedMilliseconds);
                stopwatch.Restart();
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                string? hedgingPushback = hedgingDelay.TotalMilliseconds.ToString();
                if (callCount == 1)
                {
                    hedgingPushback = "0";
                }
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: hedgingPushback);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: hedgingDelay);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            stopwatch.Start();
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => callIntervals.Count == 2, "Only two calls should be made.").DefaultTimeout();

            // First call should happen immediately
            Assert.LessOrEqual(callIntervals[0], 100);

            // Second call should happen after delay
            Assert.LessOrEqual(callIntervals[1], hedgingDelay.TotalMilliseconds);
        }

        [Test]
        public async Task AsyncUnaryCall_FatalStatusCode_HedgeDelay_Failure()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, (callCount == 1) ? StatusCode.Unavailable : StatusCode.InvalidArgument);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(hedgingDelay: TimeSpan.FromMilliseconds(50));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
            Assert.AreEqual(StatusCode.InvalidArgument, call.GetStatus().StatusCode);
            Assert.AreEqual(2, callCount);
        }

        [Test]
        public async Task AsyncServerStreamingCall_SuccessAfterRetry_RequestContentSent()
        {
            // Arrange
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            MemoryStream? requestContent = null;

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                var s = await request.Content!.ReadAsStreamAsync();
                var ms = new MemoryStream();
                await s.CopyToAsync(ms);

                if (callCount == 1)
                {
                    await syncPoint.WaitForSyncPoint();

                    await request.Content!.CopyToAsync(new MemoryStream());
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                syncPoint.Continue();

                requestContent = ms;

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 2, hedgingDelay: TimeSpan.FromMilliseconds(50));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ServerStreaming), string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
            var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

            // Wait until the first call has failed and the second is on the server
            await syncPoint.WaitToContinue().DefaultTimeout();

            // Assert
            Assert.IsTrue(await moveNextTask);
            Assert.AreEqual("Hello world", call.ResponseStream.Current.Message);

            requestContent!.Seek(0, SeekOrigin.Begin);
            var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.AreEqual("World", requestMessage!.Name);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(100)]
        public async Task AsyncClientStreamingCall_SuccessAfterRetry_RequestContentSent(int hedgingDelayMS)
        {
            // Arrange
            var callLock = new object();
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var firstCall = false;
                lock (callLock)
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        firstCall = true;
                    }
                }
                if (firstCall)
                {
                    await request.Content!.CopyToAsync(new MemoryStream());
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
                await content.PushComplete.DefaultTimeout();

                await request.Content!.CopyToAsync(requestContent);

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 2, hedgingDelay: TimeSpan.FromMilliseconds(hedgingDelayMS));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            Assert.IsNotNull(call);

            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");


            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var responseMessage = await responseTask.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            requestContent.Seek(0, SeekOrigin.Begin);

            var requests = new List<HelloRequest>();
            while (true)
            {
                var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
                if (requestMessage == null)
                {
                    break;
                }

                requests.Add(requestMessage);
            }

            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual("1", requests[0].Name);
            Assert.AreEqual("2", requests[1].Name);
        }

        [Test]
        public async Task AsyncClientStreamingCall_CompleteAndWriteAfterResult_Error()
        {
            // Arrange
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                _ = request.Content!.ReadAsStreamAsync();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            var responseMessage = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            requestContent.Seek(0, SeekOrigin.Begin);

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
            Assert.AreEqual("Request stream has already been completed.", ex.Message);
        }

        [Test]
        public async Task AsyncClientStreamingCall_WriteAfterResult_Error()
        {
            // Arrange
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                _ = request.Content!.ReadAsStreamAsync();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            var responseMessage = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
            Assert.AreEqual(StatusCode.OK, ex.StatusCode);
        }

        private static Task<HelloRequest?> ReadRequestMessage(Stream requestContent)
        {
            return StreamSerializationHelper.ReadMessageAsync(
                requestContent,
                ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                GrpcProtocolConstants.IdentityGrpcEncoding,
                maximumMessageSize: null,
                GrpcProtocolConstants.DefaultCompressionProviders,
                singleMessage: false,
                CancellationToken.None);
        }
    }
}
