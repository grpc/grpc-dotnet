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

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Google.Protobuf;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Race;
using Streaming;
using Unimplemented;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class StreamingTests : FunctionalTestBase
{
    // Big enough to hit flow control if not immediately read by peer.
    private const int BigMessageSize = 1024 * 1024 * 5;

    protected override void ConfigureServices(IServiceCollection services)
    {
        services
            .AddGrpc(options =>
            {
                options.MaxReceiveMessageSize = BigMessageSize * 2;
            });
    }

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

        await TestHelpers.AssertIsTrueRetryAsync(async () =>
        {
            try
            {
                await call.RequestStream.WriteAsync(new UnimplementeDataMessage
                {
                    Data = ByteString.CopyFrom(CreateTestData(1024 * 64))
                }).DefaultTimeout();
                return false;
            }
            catch
            {
                return true;
            }
        }, "Write until error.", Logger);

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

        using var httpEventListener = new HttpEventSourceListener(LoggerFactory);

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataComplete> ClientStreamedData(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server started");
            context.CancellationToken.Register(() =>
            {
                Logger.LogInformation("Server completed TCS");
                tcs.SetResult(null);
            });

            var total = 0L;
            await foreach (var message in requestStream.ReadAllAsync())
            {
                total += message.Data.Length;

                if (message.ServerDelayMilliseconds > 0)
                {
                    await Task.Delay(message.ServerDelayMilliseconds);
                }
            }

            return new DataComplete
            {
                Size = total
            };
        }

        // Arrange
        var data = CreateTestData(1024); // 1 KB

        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataComplete>(ClientStreamedData, "ClientStreamedDataTimeout");

        var httpClient = Fixture.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(0.5);

        var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient,
            LoggerFactory = LoggerFactory
        });

        var client = TestClientFactory.Create(channel, method);

        var dataMessage = new DataMessage
        {
            Data = ByteString.CopyFrom(data)
        };

        // Act
        var call = client.ClientStreamingCall();

        Logger.LogInformation("Client writing message");
        await call.RequestStream.WriteAsync(dataMessage).DefaultTimeout();

        Logger.LogInformation("Client waiting for TCS to complete");
        await tcs.Task.DefaultTimeout();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(dataMessage)).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.IsInstanceOf<OperationCanceledException>(call.GetStatus().DebugException);

        AssertHasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: ''.");

        await TestHelpers.AssertIsTrueRetryAsync(
            () => HasLog(LogLevel.Information, "ServiceMethodCanceled", "Service method 'ClientStreamedDataTimeout' canceled."),
            "Wait for server error so it doesn't impact other tests.").DefaultTimeout();
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
        var writeTcs = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        async Task ServerStreamingWithTrailers(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            var writeTask = Task.Run(async () =>
            {
                if (writeBeforeExit)
                {
                    await responseStream.WriteAsync(new DataMessage());
                }

                await syncPoint.WaitToContinue();

                await responseStream.WriteAsync(new DataMessage());
            });
            writeTcs.SetResult(writeTask);

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

        var writeTask = await writeTcs.Task.DefaultTimeout();
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => writeTask).DefaultTimeout();
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
        var writeTcs = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        async Task ServerStreamingWithTrailers(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            var writeTask = Task.Run(async () =>
            {
                if (writeBeforeExit)
                {
                    await responseStream.WriteAsync(new DataMessage());
                }

                await syncPoint.WaitToContinue();

                context.GetHttpContext().Abort();

                await context.CancellationToken.WaitForCancellationAsync();

                await responseStream.WriteAsync(new DataMessage());
            });
            writeTcs.SetResult(writeTask);

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

        var writeTask = await writeTcs.Task.DefaultTimeout();
        var serverException = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => writeTask).DefaultTimeout();
        Assert.AreEqual("Can't write the message because the request is complete.", serverException.Message);

        // Ensure the server abort reaches the client
        await Task.Delay(100);

        var clientException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
        Assert.AreEqual(StatusCode.Internal, clientException.StatusCode);
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
        var readTcs = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        async Task<DataMessage> ClientStreamingWithTrailers(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            var readTask = Task.Run(async () =>
            {
                if (readBeforeExit)
                {
                    Assert.IsTrue(await requestStream.MoveNext());
                }

                await syncPoint.WaitToContinue();

                Assert.IsFalse(await requestStream.MoveNext());
            });
            readTcs.SetResult(readTask);

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

        var readTask = await readTcs.Task.DefaultTimeout();
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => readTask).DefaultTimeout();
        Assert.AreEqual("Can't read messages after the request is complete.", ex.Message);

        var clientException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new DataMessage())).DefaultTimeout();
        Assert.AreEqual(StatusCode.OK, clientException.StatusCode);
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

            if (writeContext.Exception is InvalidOperationException ex &&
                ex.Message == "Can't read messages after the request is complete.")
            {
                return true;
            }

            return false;
        });

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readTcs = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        async Task<DataMessage> ClientStreamingWithTrailers(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            var readTask = Task.Run(async () =>
            {
                try
                {
                    if (readBeforeExit)
                    {
                        Assert.IsTrue(await requestStream.MoveNext());
                    }

                    await syncPoint.WaitToContinue();

                    context.GetHttpContext().Abort();

                    await context.CancellationToken.WaitForCancellationAsync();

                    Assert.IsFalse(await requestStream.MoveNext());
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Server error from reading client stream.");
                    throw;
                }
            });
            readTcs.SetResult(readTask);

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

        var readTask = await readTcs.Task.DefaultTimeout();
        var serverException = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => readTask).DefaultTimeout();
        Assert.AreEqual("Can't read messages after the request is complete.", serverException.Message);

        // Ensure the server abort reaches the client
        await Task.Delay(100);

        var clientException = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new DataMessage())).DefaultTimeout();
        Assert.AreEqual(StatusCode.Internal, clientException.StatusCode);
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

        await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) }).DefaultTimeout();

        await call.ResponseStream.MoveNext().DefaultTimeout();

        var cts = new CancellationTokenSource();
        var task = call.ResponseStream.MoveNext(cts.Token);

        cts.Cancel();

        // Assert
        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(() => task).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);
        Assert.AreEqual("Call canceled by the client.", clientEx.Status.Detail);

        var serverEx = await ExceptionAssert.ThrowsAsync<Exception>(() => tcs.Task).DefaultTimeout();
        if (serverEx is IOException || serverEx is OperationCanceledException)
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

    [Test]
    public async Task ServerStreaming_ServerErrorBeforeRead_ClientRpcError()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        Task ServerStreamingWithError(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "NotFound"));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithError);

        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var data in call.ResponseStream.ReadAllAsync())
            {
            }
        });

        // Assert
        Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);
    }

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
        var contexts = new (AsyncDuplexStreamingCall<DataMessage, DataMessage> Call, string Id, bool ReceivedOnServer)[streamCount];
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task WaitForAllStreams(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            var callId = context.RequestHeaders.GetValue("call-id");

            for (var i = 0; i < contexts.Length; i++)
            {
                if (contexts[i].Id == callId)
                {
                    contexts[i].ReceivedOnServer = true;
                }
            }
            Logger.LogInformation($"Received {callId}");

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

        try
        {
            // Act
            for (var i = 0; i < contexts.Length; i++)
            {
                var callId = (i + 1).ToString(CultureInfo.InvariantCulture);
                Logger.LogInformation($"Sending {callId}");

                var call = client.DuplexStreamingCall(new CallOptions(headers: new Metadata
                {
                    new Metadata.Entry("call-id", callId)
                }));
                contexts[i] = (call, callId, false);

                if (writeResponseHeaders)
                {
                    await call.ResponseHeadersAsync.DefaultTimeout();
                }
            }

            // Assert
            await Task.WhenAll(contexts.Select(c => c.Call.ResponseHeadersAsync)).DefaultTimeout();
        }
        catch (Exception ex)
        {
            var callIdsNotOnServer = new List<string>();
            foreach (var context in contexts)
            {
                if (!context.ReceivedOnServer)
                {
                    callIdsNotOnServer.Add(context.Id);
                }
            }

            throw new Exception($"Received {count} of {streamCount} on the server. Call IDs not received by server: {string.Join(", ", callIdsNotOnServer)}", ex);
        }
        finally
        {
            Logger.LogInformation("Test over. Disposing calls.");
            for (var i = 0; i < contexts.Length; i++)
            {
                contexts[i].Call.Dispose();
            }
        }
    }

    [Test]
    public async Task ServerStreaming_WriteAsyncCancellationBefore_ServerAbort()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCanceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ServerStreamingWithCancellation(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            await responseStream.WriteAsync(request, CancellationToken.None);
            await tcs.Task;

            try
            {
                await responseStream.WriteAsync(request, new CancellationToken(true));
            }
            catch (OperationCanceledException)
            {
                await context.CancellationToken.WaitForCancellationAsync();

                serverCanceledTcs.SetResult(context.CancellationToken.IsCancellationRequested);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithCancellation);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) });

        // Assert
        Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
        tcs.SetResult(null);

        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);

        Assert.IsTrue(await serverCanceledTcs.Task.DefaultTimeout(), "Check to see whether cancellation token is triggered on the server.");
    }

    [Test]
    public async Task ServerStreaming_WriteAsyncCancellationDuring_ServerAbort()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var firstMessageTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCanceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ServerStreamingWithCancellation(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            Logger.LogInformation("Server sending first message.");
            await responseStream.WriteAsync(request, CancellationToken.None);

            Logger.LogInformation("Server waiting for client to read first message.");
            await firstMessageTcs.Task;

            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            cts.Token.Register(() => Logger.LogInformation("CTS timer triggered cancellation."));
            try
            {
                Logger.LogInformation("Server sending big message.");
                await responseStream.WriteAsync(
                    new DataMessage { Data = ByteString.CopyFrom(new byte[BigMessageSize]) },
                    cts.Token);

                Logger.LogInformation("Server didn't wait to send big message.");
                serverCanceledTcs.SetException(new Exception("Server didn't wait to send big message."));
            }
            catch (Exception ex)
            {
                if (IsWriteCanceledException(ex))
                {
                    await context.CancellationToken.WaitForCancellationAsync();

                    Logger.LogInformation("Server got expected cancellation when sending big message.");
                    serverCanceledTcs.SetResult(context.CancellationToken.IsCancellationRequested);
                    return;
                }

                Logger.LogInformation("Server got unexpected error when sending big message.");
                serverCanceledTcs.TrySetException(ex);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingWithCancellation);

        var channel = CreateChannel(maxReceiveMessageSize: BigMessageSize * 2);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) });

        // Assert
        Logger.LogInformation("Client sending first message.");
        Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());

        Logger.LogInformation("Client waiting for server to read first message.");
        firstMessageTcs.SetResult(null);

        Logger.LogInformation("Client waiting for server cancellation confirmation.");
        var isCanceled = await serverCanceledTcs.Task.DefaultTimeout();
        Assert.IsTrue(await serverCanceledTcs.Task.DefaultTimeout());

        Logger.LogInformation("Client reading canceled message from server.");
        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();

        // Race on the server can change which error is returned.
        Assert.IsTrue(clientEx.StatusCode == StatusCode.Cancelled || clientEx.StatusCode == StatusCode.Internal);
    }

    [Test]
    public async Task ClientStreaming_WriteAsyncCancellationBefore_ClientAbort()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var firstMessageTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCanceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<DataMessage> ClientStreamingWithCancellation(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server reading first message.");
            Assert.IsTrue(await requestStream.MoveNext());
            firstMessageTcs.SetResult(null);

            try
            {
                Logger.LogInformation("Server reading second message.");
                await requestStream.MoveNext();
                throw new Exception("Should never reached here.");
            }
            catch (Exception ex)
            {
                if (IsWriteCanceledException(ex))
                {
                    await context.CancellationToken.WaitForCancellationAsync();

                    Logger.LogInformation("Server read canceled as expeceted.");
                    serverCanceledTcs.SetResult(context.CancellationToken.IsCancellationRequested);
                    return new DataMessage();
                }

                Logger.LogInformation("Server unexpected error from read.");
                serverCanceledTcs.SetException(ex);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithCancellation);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ClientStreamingCall();

        // Assert
        Logger.LogInformation("Client sending first message.");
        await call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            CancellationToken.None).DefaultTimeout();

        await firstMessageTcs.Task.DefaultTimeout();

        Logger.LogInformation("Client sending second message.");
        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(
            () => call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            new CancellationToken(true))).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);

        Logger.LogInformation("Client waiting for server canceled confirmation.");
        Assert.IsTrue(await serverCanceledTcs.Task.DefaultTimeout());
    }

    [Test]
    public async Task ClientStreaming_WriteAsyncCancellationDuring_ClientAbort()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var firstMessageTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCancellationTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCanceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<DataMessage> ClientStreamingWithCancellation(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server reading first message.");
            Assert.IsTrue(await requestStream.MoveNext());
            firstMessageTcs.SetResult(null);

            await clientCancellationTcs.Task;

            try
            {
                Logger.LogInformation("Server reading second message.");
                await requestStream.MoveNext();
                throw new Exception("Should never reached here.");
            }
            catch (Exception ex)
            {
                if (IsWriteCanceledException(ex))
                {
                    await context.CancellationToken.WaitForCancellationAsync();

                    Logger.LogInformation("Server read canceled as expeceted.");
                    serverCanceledTcs.SetResult(context.CancellationToken.IsCancellationRequested);
                    return new DataMessage();
                }

                Logger.LogInformation("Server unexpected error from read.");
                serverCanceledTcs.SetException(ex);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithCancellation);

        var channel = CreateChannel(maxReceiveMessageSize: BigMessageSize * 2);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ClientStreamingCall();

        // Assert
        Logger.LogInformation("Client sending first message.");
        await call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            CancellationToken.None).DefaultTimeout();

        await firstMessageTcs.Task.DefaultTimeout();

        Logger.LogInformation("Client sending second message.");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(
            () => call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(new byte[BigMessageSize]) },
            cts.Token)).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);

        clientCancellationTcs.SetResult(null);

        Logger.LogInformation("Client waiting for server canceled confirmation.");
        Assert.IsTrue(await serverCanceledTcs.Task.DefaultTimeout());
    }
}
