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
using System.Diagnostics;
using System.IO;
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
            var data = new byte[1024 * 1024 * 1]; // 1 MB
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)i; // Will loop around back to zero
            }
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
            var data = new byte[1024 * 64]; // 64 KB
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

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "GrpcStatusError" &&
                    writeContext.State.ToString() == "Call failed with gRPC error status. Status code: 'Unimplemented', Message: 'Service is unimplemented.'.")
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
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
                    Data = ByteString.CopyFrom(new byte[1024 * 64])
                }).DefaultTimeout();

                await call.RequestStream.WriteAsync(new UnimplementeDataMessage
                {
                    Data = ByteString.CopyFrom(new byte[1024 * 64])
                }).DefaultTimeout();
            });

            await call.ResponseHeadersAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.Unimplemented, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task DuplexStream_SendToUnimplementedMethodAfterResponseReceived_MoveNextThrowsError()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "GrpcStatusError" &&
                    writeContext.State.ToString() == "Call failed with gRPC error status. Status code: 'Unimplemented', Message: 'Service is unimplemented.'.")
                {
                    return true;
                }

                return false;
            });

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
                await call.RequestStream.CompleteAsync();

                var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext());
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
            var data = new byte[batchSize];

            var client = new StreamService.StreamServiceClient(Channel);

            var (sent, received) = await EchoData(total, data, client);

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
                await foreach (var message in call.ResponseStream.ReadAllAsync())
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
            await readTask;

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

            var data = new byte[batchSize];

            var client = new StreamService.StreamServiceClient(Channel);

            await TestHelpers.RunParallel(tasks, async () =>
            {
                var (sent, received) = await EchoData(total, data, client);

                // Assert
                Assert.AreEqual(sent, total);
                Assert.AreEqual(received, total);
            });
        }

        [Test]
        public async Task ClientStream_HttpClientWithTimeout_Success()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorStartingCall" &&
                    writeContext.Exception is TaskCanceledException)
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "GrpcStatusError" &&
                    writeContext.Message == "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'Error starting gRPC call.'.")
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
            var data = new byte[1024 * 64]; // 64 KB

            var httpClient = Fixture.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(0.5);

            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
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
    }
}
