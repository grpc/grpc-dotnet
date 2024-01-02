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

using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Streaming;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class CancellationTests : FunctionalTestBase
{
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(20)]
    public async Task DuplexStreaming_CancelAfterHeadersInParallel_Success(int tasks)
    {
        await CancelInParallel(tasks, waitForHeaders: true, interations: 10).TimeoutAfter(TimeSpan.FromSeconds(60));
    }

    [TestCase(1)]
    [TestCase(5)]
    [TestCase(20)]
    public async Task DuplexStreaming_CancelWithoutHeadersInParallel_Success(int tasks)
    {
        await CancelInParallel(tasks, waitForHeaders: false, interations: 10).TimeoutAfter(TimeSpan.FromSeconds(60));
    }

    private async Task CancelInParallel(int tasks, bool waitForHeaders, int interations)
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName)
            {
                // Kestrel cancellation error message
                if (writeContext.Exception is IOException &&
                    writeContext.Exception.Message == "The client reset the request stream.")
                {
                    return true;
                }

                // Cancellation when service is receiving message
                if (writeContext.Exception is InvalidOperationException &&
                    writeContext.Exception.Message == "Can't read messages after the request is complete.")
                {
                    return true;
                }

                // Cancellation when service is writing message
                if (writeContext.Exception is InvalidOperationException &&
                    writeContext.Exception.Message == "Can't write the message because the request is complete.")
                {
                    return true;
                }

                // Cancellation before service writes message
                if (writeContext.Exception is TaskCanceledException &&
                    writeContext.Exception.Message == "A task was canceled.")
                {
                    return true;
                }

                // Request is canceled while writing message
                if (writeContext.Exception is OperationCanceledException)
                {
                    return true;
                }
            }

            if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall")
            {
                // Cancellation when call hasn't returned headers
                if (writeContext.EventId.Name == "ErrorStartingCall" &&
                    writeContext.Exception is TaskCanceledException)
                {
                    return true;
                }
            }

            return false;
        });

        // Arrange
        var data = new byte[1024 * 64];

        var client = new StreamService.StreamServiceClient(Channel);

        await TestHelpers.RunParallel(tasks, async taskIndex =>
        {
            try
            {
                for (var i = 0; i < interations; i++)
                {
                    Logger.LogInformation($"Staring {taskIndex}-{i}");

                    var cts = new CancellationTokenSource();
                    var headers = new Metadata();
                    if (waitForHeaders)
                    {
                        headers.Add("flush-headers", bool.TrueString);
                    }
                    using var call = client.EchoAllData(cancellationToken: cts.Token, headers: headers);

                    if (waitForHeaders)
                    {
                        await call.ResponseHeadersAsync.DefaultTimeout();
                    }

                    await call.RequestStream.WriteAsync(new DataMessage
                    {
                        Data = ByteString.CopyFrom(data)
                    }).DefaultTimeout();

                    cts.Cancel();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Cancellation error");
                throw;
            }
        });

        // Wait a short amount of time so that any server cancellation error
        // finishes being thrown before the next test starts.
        await Task.Delay(50);
    }

    [Test]
    public async Task ServerStreaming_CancellationOnClientWithoutResponseHeaders_CancellationSentToServer()
    {
        var syncPoint = new SyncPoint();
        var serverCompleteTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ServerStreamingCall(DataMessage request, IServerStreamWriter<DataMessage> streamWriter, ServerCallContext context)
        {
            await syncPoint.WaitToContinue().DefaultTimeout();

            // Wait until the client cancels
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            serverCompleteTcs.TrySetResult(null);
        }

        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            // Kestrel cancellation error message
            if (writeContext.Exception is IOException &&
                writeContext.Exception.Message == "The client reset the request stream.")
            {
                return true;
            }

            if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                writeContext.EventId.Name == "ErrorStartingCall" &&
                writeContext.Message == "Error starting gRPC call.")
            {
                return true;
            }

            // Ignore all logging related errors for now
            return false;
        });

        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingCall);

        var channel = CreateChannel();
        var cts = new CancellationTokenSource();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        syncPoint.Continue();

        // Assert
        var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        await serverCompleteTcs.Task.DefaultTimeout();

        await TestHelpers.AssertIsTrueRetryAsync(
            () => HasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'Call canceled by the client.'."),
            "Missing client cancellation log.").DefaultTimeout();
    }

    [Test]
    public async Task ServerStreaming_ChannelDisposed_CancellationSentToServer()
    {
        var syncPoint = new SyncPoint();
        var serverCompleteTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ServerStreamingCall(DataMessage request, IServerStreamWriter<DataMessage> streamWriter, ServerCallContext context)
        {
            await syncPoint.WaitToContinue().DefaultTimeout();

            // Wait until the client cancels
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            }

            serverCompleteTcs.TrySetResult(null);
        }

        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            // Kestrel cancellation error message
            if (writeContext.Exception is IOException &&
                writeContext.Exception.Message == "The client reset the request stream.")
            {
                return true;
            }

            if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                writeContext.EventId.Name == "ErrorStartingCall" &&
                writeContext.Message == "Error starting gRPC call.")
            {
                return true;
            }

            // Ignore all logging related errors for now
            return false;
        });

        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingCall);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage());
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        syncPoint.Continue();

        // Assert
        await WaitForActiveCallsCountAsync(channel, 1).DefaultTimeout();
        var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

        channel.Dispose();

        await WaitForActiveCallsCountAsync(channel, 0).DefaultTimeout();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        await serverCompleteTcs.Task.DefaultTimeout();

        await TestHelpers.AssertIsTrueRetryAsync(
            () => HasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'gRPC call disposed.'."),
            "Missing client cancellation log.").DefaultTimeout();
    }

    private static async Task WaitForActiveCallsCountAsync(GrpcChannel channel, int count)
    {
        // Active calls is modified after response TCS is completed.
        // Retry a few times to ensure active calls count is updated.
        await TestHelpers.AssertIsTrueRetryAsync(() =>
        {
            return channel.GetActiveCalls().Length == count;
        }, $"Assert there are {count} active calls.");
    }

    [Test]
    public async Task ServerStreaming_CancellationOnClientAfterResponseHeadersReceived_CancellationSentToServer()
    {
        var serverCompleteTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task ServerStreamingCall(DataMessage request, IServerStreamWriter<DataMessage> streamWriter, ServerCallContext context)
        {
            try
            {
                // Write until the client cancels
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    await streamWriter.WriteAsync(new DataMessage());
                    await Task.Delay(TimeSpan.FromMilliseconds(10));
                }
            }
            catch (OperationCanceledException)
            {
                // Eat cancellation error.
            }
            finally
            {
                serverCompleteTcs.TrySetResult(null);
            }
        }

        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            // Kestrel cancellation error message
            if (writeContext.Exception is IOException &&
                writeContext.Exception.Message == "The client reset the request stream.")
            {
                return true;
            }
            if (writeContext.Exception is OperationCanceledException)
            {
                return true;
            }

            // Cancellation happened after checking token but before writing message
            if (writeContext.LoggerName == "Grpc.AspNetCore.Server.ServerCallHandler" &&
                writeContext.EventId.Name == "ErrorExecutingServiceMethod")
            {
                return true;
            }

            // Ignore all logging related errors for now
            return false;
        });

        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingCall);

        var channel = CreateChannel();
        var cts = new CancellationTokenSource();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert

        // 1. Lets read some messages
        Logger.LogInformation("Client reading message.");
        Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        Logger.LogInformation("Client reading message.");
        Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());

        // 2. Cancel the token that was passed to the gRPC call. This was given to HttpClient.SendAsync
        Logger.LogInformation("Client cancel token.");
        cts.Cancel();

        // 3. Read from the response stream. This will throw a cancellation exception locally
        Logger.LogInformation("Client reading message.");
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        // 4. Check that the cancellation was sent to the server.
        Logger.LogInformation("Client waiting for server cancellation confirmation.");
        await serverCompleteTcs.Task.DefaultTimeout();

        Logger.LogInformation("Client waiting for server cancellation log.");
        await TestHelpers.AssertIsTrueRetryAsync(
            () => HasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'Call canceled by the client.'."),
            "Missing client cancellation log.").DefaultTimeout();
    }

    [Test]
    public async Task ServerStreaming_CancellationOnClientWhileMoveNext_CancellationSentToServer()
    {
        var pauseServerTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callEndSyncPoint = new SyncPoint();
        var serverCancellationRequested = false;

        async Task ServerStreamingCall(DataMessage request, IServerStreamWriter<DataMessage> streamWriter, ServerCallContext context)
        {
            await streamWriter.WriteAsync(new DataMessage());
            await streamWriter.WriteAsync(new DataMessage());

            await pauseServerTcs.Task.DefaultTimeout();

            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(10);
            }

            serverCancellationRequested = context.CancellationToken.IsCancellationRequested;

            await callEndSyncPoint.WaitToContinue();
        }

        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingCall);

        var channel = CreateChannel();
        var cts = new CancellationTokenSource();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert

        // 1. Lets read some messages
        Logger.LogInformation("Client reading message.");
        Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        Logger.LogInformation("Client reading message.");
        Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());

        // 2. Cancel the token that was passed to the gRPC call. This should dispose HttpResponseMessage
        Logger.LogInformation("Client starting cancellation timer.");
        cts.CancelAfter(TimeSpan.FromSeconds(0.2));

        // 3. Read from the response stream. This will throw a cancellation exception locally
        Logger.LogInformation("Client waiting for cancellation.");
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        // 4. Check that the cancellation was sent to the server.
        pauseServerTcs.TrySetResult(null);

        await callEndSyncPoint.WaitForSyncPoint().DefaultTimeout();
        callEndSyncPoint.Continue();

        Assert.AreEqual(true, serverCancellationRequested);

        await TestHelpers.AssertIsTrueRetryAsync(
            () => HasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'Call canceled by the client.'."),
            "Missing client cancellation log.").DefaultTimeout();
    }

    [Test]
    public async Task Unary_CancellationDuringCall_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource();
        async Task<DataMessage> UnaryMethod(DataMessage request, ServerCallContext context)
        {
            cts.Cancel();
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task Unary_CancellationImmediately_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataMessage> UnaryMethod(DataMessage request, ServerCallContext context)
        {
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var call = client.UnaryCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task Unary_Retry_CancellationImmediately_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataMessage> UnaryMethod(DataMessage request, ServerCallContext context)
        {
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryMethod);
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true, serviceConfig: serviceConfig);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var call = client.UnaryCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ServerStreaming_CancellationDuringCall_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource();
        async Task ServerStreamingMethod(DataMessage request, IServerStreamWriter<DataMessage> writer, ServerCallContext context)
        {
            cts.Cancel();
            await tcs.Task;
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act

        var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ServerStreaming_CancellationImmediately_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ServerStreamingMethod(DataMessage request, IServerStreamWriter<DataMessage> writer, ServerCallContext context)
        {
            await tcs.Task;
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ServerStreaming_MoveNext_CancellationDuringCall_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource();
        async Task ServerStreamingMethod(DataMessage request, IServerStreamWriter<DataMessage> writer, ServerCallContext context)
        {
            cts.Cancel();
            await tcs.Task;
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new DataMessage());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseStream.MoveNext(cts.Token)).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ServerStreaming_MoveNext_CancellationImmediately_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task ServerStreamingMethod(DataMessage request, IServerStreamWriter<DataMessage> writer, ServerCallContext context)
        {
            await tcs.Task;
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var call = client.ServerStreamingCall(new DataMessage());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseStream.MoveNext(cts.Token)).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    // Support calling custom code when writing a message.
    private class SerializationCallbackMessage : IMessage
    {
        public DataMessage DataMessage { get; private set; }
        public Action? WriteCallback { get; set; }
        public MessageDescriptor Descriptor => DataMessage.Descriptor;

        public SerializationCallbackMessage()
        {
            DataMessage = new DataMessage();
        }

        public int CalculateSize() => DataMessage.CalculateSize();

        public void MergeFrom(CodedInputStream input)
        {
            DataMessage.MergeFrom(input);
        }

        public void WriteTo(CodedOutputStream output)
        {
            WriteCallback?.Invoke();
            DataMessage.WriteTo(output);
        }
    }

    [Test]
    public async Task ClientStreaming_CancellationDuringWrite_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataMessage> ClientStreamingMethod(IAsyncStreamReader<SerializationCallbackMessage> reader, ServerCallContext context)
        {
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<SerializationCallbackMessage, DataMessage>(ClientStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        var cts = new CancellationTokenSource();
        var message = new SerializationCallbackMessage { WriteCallback = cts.Cancel };

        // Act
        var call = client.ClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await call.RequestStream.WriteAsync(message);
        }).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ClientStreaming_CancellationImmediately_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataMessage> ClientStreamingMethod(IAsyncStreamReader<DataMessage> reader, ServerCallContext context)
        {
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var call = client.ClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.RequestStream.WriteAsync(new DataMessage())).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ClientStreaming_WriteAsync_CancellationDuringWrite_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataMessage> ClientStreamingMethod(IAsyncStreamReader<SerializationCallbackMessage> reader, ServerCallContext context)
        {
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<SerializationCallbackMessage, DataMessage>(ClientStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        var cts = new CancellationTokenSource();
        var message = new SerializationCallbackMessage { WriteCallback = cts.Cancel };

        // Act
        var call = client.ClientStreamingCall();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await call.RequestStream.WriteAsync(message, cts.Token);
        }).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task ClientStreaming_WriteAsync_CancellationImmediately_TokenMatchesSource()
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DataMessage> ClientStreamingMethod(IAsyncStreamReader<DataMessage> reader, ServerCallContext context)
        {
            await tcs.Task;
            return new DataMessage();
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingMethod);
        var channel = CreateChannel(throwOperationCanceledOnCancellation: true);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var call = client.ClientStreamingCall();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.RequestStream.WriteAsync(new DataMessage(), cts.Token)).DefaultTimeout();
        Assert.AreEqual(cts.Token, ex.CancellationToken);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }
}
