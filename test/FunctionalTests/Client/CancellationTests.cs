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
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Streaming;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
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
                    for (int i = 0; i < interations; i++)
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

            var channel = CreateChannel(useHandler: true);

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.ServerStreamingCall(new DataMessage());
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            syncPoint.Continue();

            // Assert
            Assert.AreEqual(1, channel.ActiveCalls.Count);
            var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

            channel.Dispose();

            Assert.AreEqual(0, channel.ActiveCalls.Count);

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

            await serverCompleteTcs.Task.DefaultTimeout();

            await TestHelpers.AssertIsTrueRetryAsync(
                () => HasLog(LogLevel.Information, "GrpcStatusError", "Call failed with gRPC error status. Status code: 'Cancelled', Message: 'gRPC call disposed.'."),
                "Missing client cancellation log.").DefaultTimeout();
        }

        [Test]
        public async Task ServerStreaming_CancellationOnClientAfterResponseHeadersReceived_CancellationSentToServer()
        {
            var serverCompleteTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task ServerStreamingCall(DataMessage request, IServerStreamWriter<DataMessage> streamWriter, ServerCallContext context)
            {
                // Write until the client cancels
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    await streamWriter.WriteAsync(new DataMessage());
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
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());

            // 2. Cancel the token that was passed to the gRPC call. This was given to HttpClient.SendAsync
            cts.Cancel();

            // 3. Read from the response stream. This will throw a cancellation exception locally
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

            // 4. Check that the cancellation was sent to the server.
            await serverCompleteTcs.Task.DefaultTimeout();

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
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());
            Assert.IsTrue(await call.ResponseStream.MoveNext(CancellationToken.None).DefaultTimeout());

            // 2. Cancel the token that was passed to the gRPC call. This should dispose HttpResponseMessage
            cts.CancelAfter(TimeSpan.FromSeconds(0.2));

            // 3. Read from the response stream. This will throw a cancellation exception locally
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
    }
}
