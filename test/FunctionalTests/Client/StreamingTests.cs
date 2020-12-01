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
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Race;
using Streaming;
using Unimplemented;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class StreamingTests : FunctionalTestBase
    {
        [Test]
        public async Task DuplexStream_SendLargeFileBatchedAndRecieveLargeFileBatched_Success()
        {
            // Arrange
            var data = CreateTestData(1024 * 1024 * 1); // 1 MB
            var client = new StreamService.StreamServiceClient(Channel);

            // Act
            var call = client.BufferAllData();

            var sent = 0;
            while (sent < data.Length)
            {
                const int BatchSize = 1024 * 64; // 64 KB

                var writeCount = Math.Min(data.Length - sent, BatchSize);

                await call.RequestStream.WriteAsync(new DataMessage
                {
                    Data = ByteString.CopyFrom(data, sent, writeCount)
                }).DefaultTimeout();

                sent += writeCount;

                Logger.LogInformation($"Sent {sent} bytes");
            }

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var ms = new MemoryStream();
            while (await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout())
            {
                ms.Write(call.ResponseStream.Current.Data.Span);

                Logger.LogInformation($"Received {ms.Length} bytes");
            }

            // Assert
            CollectionAssert.AreEqual(data, ms.ToArray());
        }

        [Test]
        public async Task ClientStream_SendLargeFileBatched_Success()
        {
            // Arrange
            var total = 1024 * 1024 * 64; // 64 MB
            var data = CreateTestData(1024 * 64); // 64 KB
            var client = new StreamService.StreamServiceClient(Channel);
            var dataMessage = new DataMessage
            {
                Data = ByteString.CopyFrom(data)
            };

            // Act
            var call = client.ClientStreamedData();

            var sent = 0;
            while (sent < total)
            {
                var writeCount = Math.Min(total - sent, data.Length);
                DataMessage m;
                if (writeCount == data.Length)
                {
                    m = dataMessage;
                }
                else
                {
                    m = new DataMessage
                    {
                        Data = ByteString.CopyFrom(data, 0, writeCount)
                    };
                }

                await call.RequestStream.WriteAsync(m).DefaultTimeout();

                sent += writeCount;
            }

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var response = await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(total, response.Size);
        }

        [Test]
        public async Task DuplexStream_SimultaneousSendAndReceive_Success()
        {
            var client = new Racer.RacerClient(Channel);

            TimeSpan raceDuration = TimeSpan.FromSeconds(1);

            var headers = new Metadata { new Metadata.Entry("race-duration", raceDuration.ToString()) };

            using (var call = client.ReadySetGo(new CallOptions(headers)))
            {
                // Read incoming messages in a background task
                RaceMessage? lastMessageReceived = null;
                var readTask = Task.Run(async () =>
                {
                    while (await call.ResponseStream.MoveNext().DefaultTimeout())
                    {
                        lastMessageReceived = call.ResponseStream.Current;
                    }
                });

                // Write outgoing messages until timer is complete
                var sw = Stopwatch.StartNew();
                var sent = 0;
                while (sw.Elapsed < raceDuration)
                {
                    await call.RequestStream.WriteAsync(new RaceMessage { Count = ++sent }).DefaultTimeout();
                }

                // Finish call and report results
                await call.RequestStream.CompleteAsync().DefaultTimeout();
                await readTask.DefaultTimeout();

                Assert.Greater(sent, 0);
                Assert.Greater(lastMessageReceived?.Count ?? 0, 0);
            }
        }

        [Test]
        public async Task DuplexStream_SendToUnimplementedMethod_ThrowError()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorSendingMessage" &&
                    writeContext.State.ToString() == "Error sending message.")
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.HttpContentClientStreamWriter" &&
                    writeContext.EventId.Name == "WriteMessageError" &&
                    writeContext.Message == "Error writing message.")
                {
                    return true;
                }

                return false;
            });

            // Arrange
            var client = new UnimplementedService.UnimplementedServiceClient(Channel);

            // Act
            var call = client.DuplexData();

            await ExceptionAssert.ThrowsAsync<Exception>(async () =>
            {
                await call.RequestStream.WriteAsync(new UnimplementeDataMessage
                {
                    Data = ByteString.CopyFrom(CreateTestData(1024 * 64))
                }).DefaultTimeout();

                await call.RequestStream.WriteAsync(new UnimplementeDataMessage
                {
                    Data = ByteString.CopyFrom(CreateTestData(1024 * 64))
                }).DefaultTimeout();
            });

            await call.ResponseHeadersAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Unimplemented, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task DuplexStream_SendToUnimplementedMethodAfterResponseReceived_MoveNextThrowsError()
        {
            // Arrange
            var client = new UnimplementedService.UnimplementedServiceClient(Channel);

            // This is in a loop to verify a hang that existed in HttpClient when the request is not read to completion
            // https://github.com/dotnet/corefx/issues/39586
            for (var i = 0; i < 100; i++)
            {
                Logger.LogInformation("Iteration " + i);

                // Act
                var call = client.DuplexData();

                // Response will only be headers so the call is "done" on the server side
                await call.ResponseHeadersAsync.DefaultTimeout();
                await call.RequestStream.CompleteAsync().DefaultTimeout();

                var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
                var status = call.GetStatus();

                // Assert
                Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
                Assert.AreEqual(StatusCode.Unimplemented, status.StatusCode);
            }
        }

        const int Size64MB = 1024 * 1024 * 64;
        const int Size1MB = 1024 * 1024 * 1;
        const int Size64KB = 1024 * 64;

        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(Size64MB, Size64KB)]
        [TestCase(Size64MB, Size1MB)]
        public async Task DuplexStreaming_SimultaniousSendAndReceive_Success(int total, int batchSize)
        {
            // Arrange
            var data = CreateTestData(batchSize);

            var client = new StreamService.StreamServiceClient(Channel);

            var (sent, received) = await EchoData(total, data, client).DefaultTimeout();

            // Assert
            Assert.AreEqual(sent, total);
            Assert.AreEqual(received, total);
        }

        private async Task<(int sent, int received)> EchoData(int total, byte[] data, StreamService.StreamServiceClient client)
        {
            var sent = 0;
            var received = 0;
            var call = client.EchoAllData();

            var readTask = Task.Run(async () =>
            {
                await foreach (var message in call.ResponseStream.ReadAllAsync().DefaultTimeout())
                {
                    received += message.Data.Length;

                    Logger.LogInformation($"Received {sent} bytes");
                }
            });

            while (sent < total)
            {
                var writeCount = Math.Min(total - sent, data.Length);

                await call.RequestStream.WriteAsync(new DataMessage
                {
                    Data = ByteString.CopyFrom(data, 0, writeCount)
                }).DefaultTimeout();

                sent += writeCount;

                Logger.LogInformation($"Sent {sent} bytes");
            }

            await call.RequestStream.CompleteAsync().DefaultTimeout();
            await readTask.DefaultTimeout();

            return (sent, received);
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(20)]
        public async Task DuplexStreaming_SimultaniousSendAndReceiveInParallel_Success(int tasks)
        {
            // Arrange
            const int total = 1024 * 1024 * 1;
            const int batchSize = 1024 * 64;

            var data = CreateTestData(batchSize);

            var client = new StreamService.StreamServiceClient(Channel);

            await TestHelpers.RunParallel(tasks, async taskIndex =>
            {
                var (sent, received) = await EchoData(total, data, client).DefaultTimeout();

                // Assert
                Assert.AreEqual(sent, total);
                Assert.AreEqual(received, total);
            }).DefaultTimeout();
        }

        [Test]
        public async Task ClientStream_HttpClientWithTimeout_Success()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.Exception is TaskCanceledException)
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "WriteMessageError" &&
                    writeContext.Exception is InvalidOperationException &&
                    writeContext.Exception.Message == "Can't write the message because the call is complete.")
                {
                    return true;
                }

                if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName)
                {
                    return true;
                }

                return false;
            });

            // Arrange
            var data = CreateTestData(1024 * 64); // 64 KB

            var httpClient = Fixture.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(0.5);

            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
            {
                HttpClient = httpClient,
                LoggerFactory = LoggerFactory
            });

            var client = new StreamService.StreamServiceClient(channel);
            var dataMessage = new DataMessage
            {
                Data = ByteString.CopyFrom(data)
            };

            // Act
            var call = client.ClientStreamedData();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () =>
            {
                while (true)
                {
                    await call.RequestStream.WriteAsync(dataMessage).DefaultTimeout();

                    await Task.Delay(100);
                }
            }).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);

            AssertHasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: ''.");
        }

        [Test]
        public async Task DuplexStreaming_ParallelCallsFromOneChannel_Success()
        {
            async Task UnaryDeadlineExceeded(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                await foreach (var message in requestStream.ReadAllAsync())
                {
                    await responseStream.WriteAsync(message);
                }
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<DataMessage, DataMessage>(UnaryDeadlineExceeded);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call1 = client.DuplexStreamingCall();
            var call2 = client.DuplexStreamingCall();

            await call1.RequestStream.WriteAsync(new DataMessage() { Data = ByteString.CopyFrom(new byte[1]) }).DefaultTimeout();
            await call2.RequestStream.WriteAsync(new DataMessage() { Data = ByteString.CopyFrom(new byte[2]) }).DefaultTimeout();

            // Assert
            Assert.IsTrue(await call1.ResponseStream.MoveNext().DefaultTimeout());
            Assert.IsTrue(await call2.ResponseStream.MoveNext().DefaultTimeout());

            Assert.AreEqual(1, call1.ResponseStream.Current.Data.Length);
            Assert.AreEqual(2, call2.ResponseStream.Current.Data.Length);

            await call1.RequestStream.CompleteAsync().DefaultTimeout();
            await call2.RequestStream.CompleteAsync().DefaultTimeout();

            Assert.IsFalse(await call1.ResponseStream.MoveNext().DefaultTimeout());
            Assert.IsFalse(await call2.ResponseStream.MoveNext().DefaultTimeout());
        }

        [Test]
        public async Task ServerStreaming_GetTrailersAndStatus_Success()
        {
            async Task ServerStreamingWithTrailers(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                await responseStream.WriteAsync(new DataMessage());
                context.ResponseTrailers.Add("my-trailer", "value");
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithTrailers);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ServerStreamingCall(new DataMessage());

            // Assert
            Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());

            Assert.AreEqual(0, call.ResponseStream.Current.Data.Length);

            Assert.IsFalse(await call.ResponseStream.MoveNext().DefaultTimeout());

            var trailers = call.GetTrailers();
            Assert.AreEqual(1, trailers.Count);
            Assert.AreEqual("value", trailers.GetValue("my-trailer"));

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ServerStreaming_WriteAfterMethodComplete_Error(bool writeBeforeExit)
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            Task? writeTask = null;
            async Task ServerStreamingWithTrailers(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                writeTask = Task.Run(async () =>
                {
                    if (writeBeforeExit)
                    {
                        await responseStream.WriteAsync(new DataMessage());
                    }

                    await syncPoint.WaitToContinue();

                    await responseStream.WriteAsync(new DataMessage());
                });

                await tcs.Task;
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithTrailers);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ServerStreamingCall(new DataMessage());

            await syncPoint.WaitForSyncPoint().DefaultTimeout();

            tcs.SetResult(null);

            // Assert
            if (writeBeforeExit)
            {
                Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
            }

            Assert.IsFalse(await call.ResponseStream.MoveNext().DefaultTimeout());
            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);

            syncPoint.Continue();

            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(async () => await writeTask!.DefaultTimeout());
            Assert.AreEqual("Can't write the message because the request is complete.", ex.Message);

            Assert.IsFalse(await call.ResponseStream.MoveNext());
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ServerStreaming_WriteAfterMethodCancelled_Error(bool writeBeforeExit)
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    (writeContext.Exception is TaskCanceledException || writeContext.Exception is HttpRequestException))
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorReadingMessage")
                {
                    return true;
                }


                return false;
            });

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            Task? writeTask = null;
            async Task ServerStreamingWithTrailers(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                writeTask = Task.Run(async () =>
                {
                    if (writeBeforeExit)
                    {
                        await responseStream.WriteAsync(new DataMessage());
                    }

                    await syncPoint.WaitToContinue();

                    context.GetHttpContext().Abort();

                    await responseStream.WriteAsync(new DataMessage());
                });

                await tcs.Task;
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithTrailers);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ServerStreamingCall(new DataMessage());

            await syncPoint.WaitForSyncPoint().DefaultTimeout();

            // Assert
            if (writeBeforeExit)
            {
                Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
            }

            syncPoint.Continue();

            var serverException = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => writeTask!).DefaultTimeout();
            Assert.AreEqual("Can't write the message because the request is complete.", serverException.Message);

            // Ensure the server abort reaches the client
            await Task.Delay(100);

            var clientException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, clientException.StatusCode);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ClientStreaming_ReadAfterMethodComplete_Error(bool readBeforeExit)
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.HttpContentClientStreamWriter" &&
                    writeContext.Exception is InvalidOperationException)
                {
                    return true;
                }

                return false;
            });

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            Task? readTask = null;
            async Task<DataMessage> ClientStreamingWithTrailers(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
            {
                readTask = Task.Run(async () =>
                {
                    if (readBeforeExit)
                    {
                        Assert.IsTrue(await requestStream.MoveNext());
                    }

                    await syncPoint.WaitToContinue();

                    Assert.IsFalse(await requestStream.MoveNext());
                });

                await tcs.Task;
                return new DataMessage();
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithTrailers);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ClientStreamingCall();

            // Assert
            if (readBeforeExit)
            {
                await call.RequestStream.WriteAsync(new DataMessage()).DefaultTimeout();
            }

            await syncPoint.WaitForSyncPoint().DefaultTimeout();

            tcs.SetResult(null);

            var response = await call;

            syncPoint.Continue();

            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => readTask!).DefaultTimeout();
            Assert.AreEqual("Can't read messages after the request is complete.", ex.Message);

            var clientException = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new DataMessage())).DefaultTimeout();
            Assert.AreEqual("Can't write the message because the call is complete.", clientException.Message);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ClientStreaming_ReadAfterMethodCancelled_Error(bool readBeforeExit)
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    (writeContext.Exception is TaskCanceledException || writeContext.Exception is HttpRequestException))
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.HttpContentClientStreamWriter" &&
                    writeContext.Exception is InvalidOperationException)
                {
                    return true;
                }


                return false;
            });

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            Task? readTask = null;
            async Task<DataMessage> ClientStreamingWithTrailers(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
            {
                readTask = Task.Run(async () =>
                {
                    if (readBeforeExit)
                    {
                        Assert.IsTrue(await requestStream.MoveNext());
                    }

                    await syncPoint.WaitToContinue();

                    context.GetHttpContext().Abort();

                    Assert.IsFalse(await requestStream.MoveNext());
                });

                await tcs.Task;
                return new DataMessage();
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithTrailers);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ClientStreamingCall();

            // Assert
            if (readBeforeExit)
            {
                await call.RequestStream.WriteAsync(new DataMessage()).DefaultTimeout();
            }

            await syncPoint.WaitForSyncPoint().DefaultTimeout();

            syncPoint.Continue();

            var serverException = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => readTask!).DefaultTimeout();
            Assert.AreEqual("Can't read messages after the request is complete.", serverException.Message);

            // Ensure the server abort reaches the client
            await Task.Delay(100);

            var clientException = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new DataMessage())).DefaultTimeout();
            Assert.AreEqual("Can't write the message because the call is complete.", clientException.Message);
        }

        [Test]
        public async Task ServerStreaming_ThrowErrorWithTrailers_TrailersReturnedToClient()
        {
            async Task ServerStreamingWithTrailers(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                await context.WriteResponseHeadersAsync(new Metadata
                {
                    { "Key", "Value1" },
                    { "Key", "Value2" },
                });
                await responseStream.WriteAsync(new DataMessage());
                await responseStream.WriteAsync(new DataMessage());
                await responseStream.WriteAsync(new DataMessage());
                await responseStream.WriteAsync(new DataMessage());
                context.ResponseTrailers.Add("Key", "ResponseTrailers");
                throw new RpcException(new Status(StatusCode.Aborted, "Message"), new Metadata
                {
                    { "Key", "RpcException" }
                });
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithTrailers);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ServerStreamingCall(new DataMessage());

            // Assert
            var headers = await call.ResponseHeadersAsync.DefaultTimeout();
            var keyHeaders = headers.GetAll("key").ToList();
            Assert.AreEqual("key", keyHeaders[0].Key);
            Assert.AreEqual("Value1", keyHeaders[0].Value);
            Assert.AreEqual("key", keyHeaders[1].Key);
            Assert.AreEqual("Value2", keyHeaders[1].Value);

            Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
            Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
            Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
            Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();

            var trailers = call.GetTrailers();
            Assert.AreEqual(2, trailers.Count);
            Assert.AreEqual("key", trailers[0].Key);
            Assert.AreEqual("ResponseTrailers", trailers[0].Value);
            Assert.AreEqual("key", trailers[1].Key);
            Assert.AreEqual("RpcException", trailers[1].Value);

            Assert.AreEqual(StatusCode.Aborted, call.GetStatus().StatusCode);
            Assert.AreEqual("Message", call.GetStatus().Detail);
        }

        [Test]
        public async Task DuplexStreaming_CancelResponseMoveNext_CancellationSentToServer()
        {
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task DuplexStreamingWithCancellation(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                try
                {
                    await foreach (var message in requestStream.ReadAllAsync())
                    {
                        await responseStream.WriteAsync(message);
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<DataMessage, DataMessage>(DuplexStreamingWithCancellation);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.DuplexStreamingCall();

            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) });

            await call.ResponseStream.MoveNext();

            var cts = new CancellationTokenSource();
            var task = call.ResponseStream.MoveNext(cts.Token);

            cts.Cancel();

            // Assert
            var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(() => task);
            Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);
            Assert.AreEqual("Call canceled by the client.", clientEx.Status.Detail);

            var serverEx = await ExceptionAssert.ThrowsAsync<Exception>(() => tcs.Task);
            if (serverEx is IOException)
            {
                // Cool
            }
            else if (serverEx is InvalidOperationException)
            {
                Assert.AreEqual("Can't read messages after the request is complete.", serverEx.Message);
            }
            else
            {
                Assert.Fail();
            }
        }

        private static byte[] CreateTestData(int size)
        {
            var data = new byte[size];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i; // Will loop around back to zero
            }
            return data;
        }

#if NET5_0
        [Test]
        public Task MaxConcurrentStreams_StartConcurrently_AdditionalConnectionsCreated()
        {
            return RunConcurrentStreams(writeResponseHeaders: false);
        }

        [Test]
        public Task MaxConcurrentStreams_StartIndividually_AdditionalConnectionsCreated()
        {
            return RunConcurrentStreams(writeResponseHeaders: true);
        }

        private async Task RunConcurrentStreams(bool writeResponseHeaders)
        {
            var streamCount = 201;
            var count = 0;
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task WaitForAllStreams(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                Interlocked.Increment(ref count);

                if (writeResponseHeaders)
                {
                    await context.WriteResponseHeadersAsync(new Metadata());
                }

                if (count >= streamCount)
                {
                    tcs.TrySetResult(null);
                }

                await tcs.Task;
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<DataMessage, DataMessage>(WaitForAllStreams);

            var channel = GrpcChannel.ForAddress(Fixture.GetUrl(TestServerEndpointName.Http2));

            var client = TestClientFactory.Create(channel, method);

            var calls = new AsyncDuplexStreamingCall<DataMessage, DataMessage>[streamCount];
            try
            {
                // Act
                for (var i = 0; i < calls.Length; i++)
                {
                    var call = client.DuplexStreamingCall();
                    calls[i] = call;

                    if (writeResponseHeaders)
                    {
                        await call.ResponseHeadersAsync.DefaultTimeout();
                    }
                }

                // Assert
                await Task.WhenAll(calls.Select(c => c.ResponseHeadersAsync)).DefaultTimeout();
            }
            catch (Exception ex)
            {
                throw new Exception($"Received {count} of {streamCount} on the server.", ex);
            }
            finally
            {
                for (var i = 0; i < calls.Length; i++)
                {
                    calls[i].Dispose();
                }
            }
        }
#endif
    }
}
