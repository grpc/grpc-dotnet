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
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Streaming;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class HedgingTests : FunctionalTestBase
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

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(20)]
    public async Task Unary_ExceedAttempts_Failure(int? hedgingDelay)
    {
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, "")));
        }

        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var delay = (hedgingDelay == null)
            ? (TimeSpan?)null
            : TimeSpan.FromMilliseconds(hedgingDelay.Value);
        var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: delay));

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: ExceededAttemptCount");
    }

    [Test]
    public async Task Duplex_ManyParallelRequests_MessageRoundTripped()
    {
        const string ImportantMessage =
@"       _____  _____   _____ 
       |  __ \|  __ \ / ____|
   __ _| |__) | |__) | |     
  / _` |  _  /|  ___/| |     
 | (_| | | \ \| |    | |____ 
  \__, |_|  \_\_|     \_____|
   __/ |                     
  |___/                      
  _                          
 (_)                         
  _ ___                      
 | / __|                     
 | \__ \          _          
 |_|___/         | |         
   ___ ___   ___ | |         
  / __/ _ \ / _ \| |         
 | (_| (_) | (_) | |         
  \___\___/ \___/|_|         
                             
                             ";

        var attempts = 100;
        var allUploads = new List<string>();
        var allCompletedTasks = new List<Task>();
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task MessageUpload(
            IAsyncStreamReader<StringValue> requestStream,
            IServerStreamWriter<StringValue> responseStream,
            ServerCallContext context)
        {
            // Receive chunks
            var chunks = new List<string>();
            await foreach (var chunk in requestStream.ReadAllAsync())
            {
                chunks.Add(chunk.Value);
            }

            Task completeTask;
            lock (allUploads)
            {
                allUploads.Add(string.Join(Environment.NewLine, chunks));
                if (allUploads.Count < attempts)
                {
                    // Check that unused calls are canceled.
                    completeTask = Task.Run(async () =>
                    {
                        await tcs.Task;

                        var cancellationTcs = new TaskCompletionSource<bool>();
                        context.CancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).SetResult(true), cancellationTcs);
                        await cancellationTcs.Task;
                    });
                }
                else
                {
                    // Write response in used call.
                    completeTask = Task.Run(async () =>
                    {
                        // Write chunks
                        foreach (var chunk in chunks)
                        {
                            await responseStream.WriteAsync(new StringValue
                            {
                                Value = chunk
                            });
                        }
                    });
                }
            }

            await completeTask;
        }

        var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<StringValue, StringValue>(MessageUpload);

        var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 100, hedgingDelay: TimeSpan.Zero), maxRetryAttempts: 100);

        var client = TestClientFactory.Create(channel, method);

        using var call = client.DuplexStreamingCall();

        var lines = ImportantMessage.Split(Environment.NewLine);
        for (var i = 0; i < lines.Length; i++)
        {
            await call.RequestStream.WriteAsync(new StringValue { Value = lines[i] }).DefaultTimeout();
            await Task.Delay(TimeSpan.FromSeconds(0.01)).DefaultTimeout();
        }
        await call.RequestStream.CompleteAsync().DefaultTimeout();

        await TestHelpers.AssertIsTrueRetryAsync(() => allUploads.Count == 100, "Wait for all calls to reach server.").DefaultTimeout();
        tcs.SetResult(null);

        var receivedLines = new List<string>();
        await foreach (var line in call.ResponseStream.ReadAllAsync().DefaultTimeout())
        {
            receivedLines.Add(line.Value);
        }

        Assert.AreEqual(ImportantMessage, string.Join(Environment.NewLine, receivedLines));

        foreach (var upload in allUploads)
        {
            Assert.AreEqual(ImportantMessage, upload);
        }

        await Task.WhenAll(allCompletedTasks).DefaultTimeout();
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task Unary_DeadlineExceedAfterServerCall_Failure(int exceptedServerCallCount)
    {
        var callCount = 0;
        var tcs = new TaskCompletionSource<DataMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            callCount++;

            if (callCount < exceptedServerCallCount)
            {
                return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.DeadlineExceeded, "")));
            }

            return tcs.Task;
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(nonFatalStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
        var channel = CreateChannel(serviceConfig: serviceConfig);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(200)));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
        Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);

        Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));
    }

    [Test]
    public async Task Unary_DeadlineExceedDuringDelay_Failure()
    {
        var callCount = 0;
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            callCount++;

            return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.DeadlineExceeded, ""), new Metadata
            {
                new Metadata.Entry(GrpcProtocolConstants.RetryPushbackHeader, TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
            }));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(
            hedgingDelay: TimeSpan.FromSeconds(10),
            nonFatalStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
        var channel = CreateChannel(serviceConfig: serviceConfig);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(300)));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
        Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        Assert.AreEqual(1, callCount);

        Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));

        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: DeadlineExceeded");
    }

    [Test]
    public async Task Duplex_DeadlineExceedDuringDelay_Failure()
    {
        var callCount = 0;
        Task DuplexDeadlineExceeded(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
        {
            callCount++;

            return Task.FromException(new RpcException(new Status(StatusCode.DeadlineExceeded, ""), new Metadata
            {
                new Metadata.Entry(GrpcProtocolConstants.RetryPushbackHeader, TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture))
            }));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<DataMessage, DataMessage>(DuplexDeadlineExceeded);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(
            hedgingDelay: TimeSpan.FromSeconds(10),
            nonFatalStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
        var channel = CreateChannel(serviceConfig: serviceConfig);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var deadlineTimeout = 500;
        var call = client.DuplexStreamingCall(new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(deadlineTimeout)));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);

        ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new DataMessage())).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);

        Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        Assert.AreEqual(1, callCount);

        Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));
    }

    [Test]
    public async Task Unary_DeadlineExceedBeforeServerCall_Failure()
    {
        var callCount = 0;
        var tcs = new TaskCompletionSource<DataMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            callCount++;
            return tcs.Task;
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(nonFatalStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
        var channel = CreateChannel(serviceConfig: serviceConfig);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
        Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        Assert.AreEqual(0, callCount);

        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: DeadlineExceeded");

        tcs.SetResult(new DataMessage());
    }

    [Test]
    public async Task Unary_CanceledBeforeServerCall_Failure()
    {
        var callCount = 0;
        var tcs = new TaskCompletionSource<DataMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            callCount++;
            return tcs.Task;
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(nonFatalStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
        var channel = CreateChannel(serviceConfig: serviceConfig);

        var client = TestClientFactory.Create(channel, method);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var call = client.UnaryCall(new DataMessage(), new CallOptions(cancellationToken: cts.Token));

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual(0, callCount);

        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: Canceled");

        tcs.SetResult(new DataMessage());
    }

    [Test]
    public async Task Unary_TriggerRetryThrottling_Failure()
    {
        var callCount = 0;
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            callCount++;
            return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, "")));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(
            hedgingDelay: TimeSpan.FromSeconds(10),
            retryThrottling: new RetryThrottlingPolicy
            {
                MaxTokens = 5,
                TokenRatio = 0.1
            }));

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

        Assert.AreEqual(3, callCount);
        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: Throttled");
    }

    [TestCase(0)]
    [TestCase(100)]
    public async Task Unary_RetryThrottlingAlreadyActive_Failure(int hedgingDelayMilliseconds)
    {
        var callCount = 0;
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            callCount++;
            return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, "")));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(
            hedgingDelay: TimeSpan.FromMilliseconds(hedgingDelayMilliseconds),
            retryThrottling: new RetryThrottlingPolicy
            {
                MaxTokens = 5,
                TokenRatio = 0.1
            }));

        // Manually trigger retry throttling
        Debug.Assert(channel.RetryThrottling != null);
        channel.RetryThrottling.CallFailure();
        channel.RetryThrottling.CallFailure();
        channel.RetryThrottling.CallFailure();
        Debug.Assert(channel.RetryThrottling.IsRetryThrottlingActive());

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

        Assert.AreEqual(1, callCount);
        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: Throttled");
    }

    [Test]
    public async Task Unary_RetryThrottlingBecomesActive_HasDelay_Failure()
    {
        var callCount = 0;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        async Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            Interlocked.Increment(ref callCount);
            await syncPoint.WaitToContinue();
            return request;
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(
            hedgingDelay: TimeSpan.FromMilliseconds(100),
            retryThrottling: new RetryThrottlingPolicy
            {
                MaxTokens = 5,
                TokenRatio = 0.1
            }));

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage());

        await syncPoint.WaitForSyncPoint().DefaultTimeout();

        // Manually trigger retry throttling
        Debug.Assert(channel.RetryThrottling != null);
        channel.RetryThrottling.CallFailure();
        channel.RetryThrottling.CallFailure();
        channel.RetryThrottling.CallFailure();
        Debug.Assert(channel.RetryThrottling.IsRetryThrottlingActive());

        // Assert
        await TestHelpers.AssertIsTrueRetryAsync(() => HasLog(LogLevel.Debug, "AdditionalCallsBlockedByRetryThrottling", "Additional calls blocked by retry throttling."), "Check for expected log.");

        Assert.AreEqual(1, callCount);
        syncPoint.Continue();

        await call.ResponseAsync.DefaultTimeout();
        Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);

        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: ResponseHeadersReceived");
    }

    [TestCase(0)]
    [TestCase(20)]
    public async Task Unary_AttemptsGreaterThanDefaultClientLimit_LimitedAttemptsMade(int hedgingDelay)
    {
        var callCount = 0;
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, "")));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 10, hedgingDelay: TimeSpan.FromMilliseconds(hedgingDelay)));

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

        Assert.AreEqual(5, callCount);

        AssertHasLog(LogLevel.Debug, "MaxAttemptsLimited", "The method has 10 attempts specified in the service config. The number of attempts has been limited by channel configuration to 5.");
        AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: ExceededAttemptCount");
    }

    [TestCase(0, false, 0)]
    [TestCase(0, false, 1)]
    [TestCase(GrpcChannel.DefaultMaxRetryBufferPerCallSize - 10, false, 0)] // Final message size is bigger because of header + Protobuf field
    [TestCase(GrpcChannel.DefaultMaxRetryBufferPerCallSize - 10, false, 1)] // Final message size is bigger because of header + Protobuf field
    [TestCase(GrpcChannel.DefaultMaxRetryBufferPerCallSize + 10, true, 0)]
    [TestCase(GrpcChannel.DefaultMaxRetryBufferPerCallSize + 10, true, 1)]
    public async Task Unary_LargeMessages_ExceedPerCallBufferSize(long payloadSize, bool exceedBufferLimit, int hedgingDelayMilliseconds)
    {
        var callCount = 0;
        Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
        {
            Interlocked.Increment(ref callCount);
            return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, "")));
        }

        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.EventId.Name == "ErrorSendingMessage" ||
                writeContext.EventId.Name == "ErrorExecutingServiceMethod")
            {
                return true;
            }

            return false;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

        var channel = CreateChannel(
            serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(hedgingDelay: TimeSpan.FromMilliseconds(hedgingDelayMilliseconds)),
            maxReceiveMessageSize: (int)GrpcChannel.DefaultMaxRetryBufferPerCallSize * 2);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new DataMessage
        {
            Data = ByteString.CopyFrom(new byte[payloadSize])
        });

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

        if (!exceedBufferLimit)
        {
            Assert.AreEqual(5, callCount);
        }
        else
        {
            Assert.AreEqual(1, callCount);
            AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: BufferExceeded");

            // Cancelled calls could cause server errors. Delay so these error don't show up
            // in the next unit test.
            await Task.Delay(100);
        }

        Assert.AreEqual(0, channel.CurrentRetryBufferSize);

    }

    [Test]
    public async Task ClientStreaming_WriteLargeMessageCausingCommit_Success()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var firstMessageTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientCancellationTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverCanceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        const int maxAttempts = 2;
        var callCount = 0;
        var serverSuccessCount = 0;
        var serverAbortCount = 0;
        async Task<DataMessage> ClientStreamingWithCancellation(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server reading first message.");
            Assert.IsTrue(await requestStream.MoveNext());
            if (Interlocked.Increment(ref callCount) >= maxAttempts)
            {
                firstMessageTcs.TrySetResult(null);
            }

            try
            {
                await requestStream.MoveNext();
                Interlocked.Increment(ref serverSuccessCount);
                return requestStream.Current;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref serverAbortCount);
                serverCanceledTcs.TrySetException(ex);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithCancellation);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(
            maxAttempts: maxAttempts);
        var channel = CreateChannel(serviceConfig: serviceConfig, maxReceiveMessageSize: BigMessageSize * 2);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ClientStreamingCall();

        // Assert
        Logger.LogInformation("Client sending first message.");
        await call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            CancellationToken.None).DefaultTimeout();

        await firstMessageTcs.Task.DefaultTimeout();

        // This large message causes the call to be commited and cancels other calls.
        Logger.LogInformation("Client sending large message.");
        var writeLargeMessageTask = call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(new byte[BigMessageSize]) });

        var response = await call.ResponseAsync.DefaultTimeout();
        Assert.AreEqual(BigMessageSize, response.Data.Length);

        await ExceptionAssert.ThrowsAsync<Exception>(() => serverCanceledTcs.Task).DefaultTimeout();
        Assert.AreEqual(1, serverSuccessCount);
        Assert.AreEqual(1, serverAbortCount);
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

        var maxAttempts = 2;
        var callCount = 0;
        async Task<DataMessage> ClientStreamingWithCancellation(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Assert.IsTrue(await requestStream.MoveNext());
            if (Interlocked.Increment(ref callCount) >= maxAttempts)
            {
                firstMessageTcs.TrySetResult(null);
            }

            try
            {
                await requestStream.MoveNext();
                throw new Exception("Should never reached here.");
            }
            catch (IOException)
            {
                await context.CancellationToken.WaitForCancellationAsync();

                serverCanceledTcs.TrySetResult(context.CancellationToken.IsCancellationRequested);
                return new DataMessage();
            }
            catch (Exception ex)
            {
                serverCanceledTcs.TrySetException(ex);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithCancellation);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(
            maxAttempts: maxAttempts);
        var channel = CreateChannel(serviceConfig: serviceConfig);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ClientStreamingCall();

        // Assert
        await call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            CancellationToken.None).DefaultTimeout();

        await firstMessageTcs.Task.DefaultTimeout();

        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(
            () => call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            new CancellationToken(true))).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);

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

        var maxAttempts = 2;
        var callCount = 0;
        async Task<DataMessage> ClientStreamingWithCancellation(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server reading first message.");
            Assert.IsTrue(await requestStream.MoveNext());
            if (Interlocked.Increment(ref callCount) >= maxAttempts)
            {
                firstMessageTcs.TrySetResult(null);
            }

            Logger.LogInformation("Server waiting for canceled client message.");
            await clientCancellationTcs.Task;

            try
            {
                await requestStream.MoveNext();
                throw new Exception("Should never reached here.");
            }
            catch (Exception ex)
            {
                if (IsWriteCanceledException(ex))
                {
                    await context.CancellationToken.WaitForCancellationAsync();

                    serverCanceledTcs.SetResult(context.CancellationToken.IsCancellationRequested);
                    return new DataMessage();
                }

                serverCanceledTcs.TrySetException(ex);
                throw;
            }
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithCancellation);

        var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(
            maxAttempts: maxAttempts);
        var channel = CreateChannel(
            serviceConfig: serviceConfig,
            maxReceiveMessageSize: BigMessageSize * 2);

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ClientStreamingCall();

        // Assert
        Logger.LogInformation("Client sending first message.");
        await call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(Encoding.UTF8.GetBytes("Hello world")) },
            CancellationToken.None).DefaultTimeout();

        await firstMessageTcs.Task.DefaultTimeout();

        Logger.LogInformation("Client sending large message.");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var clientEx = await ExceptionAssert.ThrowsAsync<RpcException>(
            () => call.RequestStream.WriteAsync(
            new DataMessage { Data = ByteString.CopyFrom(new byte[BigMessageSize]) },
            cts.Token)).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, clientEx.StatusCode);

        clientCancellationTcs.SetResult(null);

        Assert.IsTrue(await serverCanceledTcs.Task.DefaultTimeout());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ClientStreaming_WriteAsyncCancellationDuringRetry_Canceled(bool throwOperationCanceledOnCancellation)
    {
        async Task<DataMessage> ClientStreamingWithReadFailures(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server reading message 1.");
            Assert.IsTrue(await requestStream.MoveNext());

            Logger.LogInformation("Server pausing.");
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            Logger.LogInformation("Server erroring.");
            throw new RpcException(new Status(StatusCode.Unavailable, string.Empty));
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithReadFailures);
        var channel = CreateChannel(
            serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: TimeSpan.FromSeconds(20)),
            maxReceiveMessageSize: BigMessageSize * 2,
            maxRetryBufferPerCallSize: BigMessageSize * 2,
            throwOperationCanceledOnCancellation: throwOperationCanceledOnCancellation);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        var call = client.ClientStreamingCall();

        Logger.LogInformation("Client writing message 1.");
        await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { (byte)1 }) }, cts.Token).DefaultTimeout();

        Logger.LogInformation("Client writing message 2.");
        var writeTask = call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[BigMessageSize]) }, cts.Token);

        // Assert
        if (throwOperationCanceledOnCancellation)
        {
            var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => writeTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        }
        else
        {
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => writeTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        }
        Assert.IsTrue(cts.Token.IsCancellationRequested, "WriteAsync finished when CancellationToken wasn't triggered.");
    }

    [Test]
    public async Task ClientStreaming_WriteAsyncFailsUntilRetries_WriteAsyncAwaitsUntilSuccess()
    {
        Task? largeWriteTask = null;

        var callCount = 0;
        bool? clientWriteWaitedForServerRead = null;
        async Task<DataMessage> ClientStreamingWithReadFailures(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
        {
            Logger.LogInformation("Server reading message 1.");
            Assert.IsTrue(await requestStream.MoveNext());

            var currentCallCount = Interlocked.Increment(ref callCount);
            Logger.LogInformation("Server current call count: " + currentCallCount);

            if (currentCallCount <= 2)
            {
                Logger.LogInformation("Server pausing.");
                await Task.Delay(TimeSpan.FromMilliseconds(500));

                Logger.LogInformation("Server erroring.");
                throw new RpcException(new Status(StatusCode.Unavailable, string.Empty));
            }
            else
            {
                clientWriteWaitedForServerRead = !largeWriteTask!.IsCompleted;

                Logger.LogInformation("Server reading message 2.");
                Assert.IsTrue(await requestStream.MoveNext());

                Logger.LogInformation("Server sending response.");
                return new DataMessage();
            }
        }

        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        // Arrange
        var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithReadFailures);
        var channel = CreateChannel(
            serviceConfig: ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: TimeSpan.FromMinutes(20)),
            maxReceiveMessageSize: BigMessageSize * 2,
            maxRetryBufferPerCallSize: BigMessageSize * 2);
        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ClientStreamingCall();

        Logger.LogInformation("Client writing message 1.");
        await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { (byte)1 }) }).DefaultTimeout();

        Logger.LogInformation("Client writing message 2.");
        largeWriteTask = call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[BigMessageSize]) });

        await largeWriteTask.DefaultTimeout();

        // Assert
        Logger.LogInformation("Client waiting for response.");
        var response = await call.ResponseAsync.DefaultTimeout();
        Assert.IsTrue(clientWriteWaitedForServerRead);
    }
}
