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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Streaming;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class RetryTests : FunctionalTestBase
    {
        [Test]
        public async Task ClientStreaming_MultipleWritesAndRetries_Failure()
        {
            var nextFailure = 1;

            async Task<DataMessage> ClientStreamingWithReadFailures(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
            {
                List<byte> bytes = new List<byte>();
                await foreach (var message in requestStream.ReadAllAsync())
                {
                    if (bytes.Count >= nextFailure)
                    {
                        nextFailure = nextFailure * 2;
                        throw new RpcException(new Status(StatusCode.Unavailable, ""));
                    }

                    bytes.Add(message.Data[0]);
                }

                return new DataMessage
                {
                    Data = ByteString.CopyFrom(bytes.ToArray())
                };
            }

            SetExpectedErrorsFilter(writeContext =>
            {
                return true;
            });

            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithReadFailures);
            var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig(maxAttempts: 10), maxRetryAttempts: 10);
            var client = TestClientFactory.Create(channel, method);
            var sentData = new List<byte>();

            // Act
            var call = client.ClientStreamingCall();

            for (var i = 0; i < 20; i++)
            {
                sentData.Add((byte)i);

                await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { (byte)i }) }).DefaultTimeout();
                await Task.Delay(1);
            }

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var result = await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.IsTrue(result.Data.Span.SequenceEqual(sentData.ToArray()));
        }

        [Test]
        public async Task Unary_ExceedRetryAttempts_Failure()
        {
            Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
            {
                var metadata = new Metadata();
                metadata.Add("grpc-retry-pushback-ms", "5");

                return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, ""), metadata));
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

            var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig());

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

            AssertHasLog(LogLevel.Debug, "RetryPushbackReceived", "Retry pushback of '5' received from the failed gRPC call.");
            AssertHasLog(LogLevel.Debug, "RetryEvaluated", "Evaluated retry for failed gRPC call. Status code: 'Unavailable', Attempt: 1, Retry: True");
            AssertHasLog(LogLevel.Trace, "StartingRetryDelay", "Starting retry delay of 00:00:00.0050000.");
            AssertHasLog(LogLevel.Debug, "RetryEvaluated", "Evaluated retry for failed gRPC call. Status code: 'Unavailable', Attempt: 5, Retry: False");
            AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: ExceededAttemptCount");
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

            var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig(
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

            AssertHasLog(LogLevel.Debug, "RetryEvaluated", "Evaluated retry for failed gRPC call. Status code: 'Unavailable', Attempt: 3, Retry: False");
            AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: Throttled");
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

            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(retryableStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
            var channel = CreateChannel(serviceConfig: serviceConfig);

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(200)));

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            Assert.AreEqual(exceptedServerCallCount, callCount);

            Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));

            AssertHasLog(LogLevel.Debug, "CallCommited", "Call commited. Reason: DeadlineExceeded");

            tcs.SetResult(new DataMessage());
        }

        [Test]
        public async Task Unary_DeadlineExceedDuringBackoff_Failure()
        {
            var callCount = 0;
            Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
            {
                callCount++;

                return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, ""), new Metadata
                {
                    new Metadata.Entry("grpc-retry-pushback-ms", TimeSpan.FromSeconds(10).TotalMilliseconds.ToString())
                }));
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(
                initialBackoff: TimeSpan.FromSeconds(10),
                maxBackoff: TimeSpan.FromSeconds(10),
                retryableStatusCodes: new List<StatusCode> { StatusCode.Unavailable });
            var channel = CreateChannel(serviceConfig: serviceConfig);

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(500)));

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            Assert.AreEqual(1, callCount);

            Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));
        }

        [Test]
        public async Task Duplex_DeadlineExceedDuringBackoff_Failure()
        {
            var callCount = 0;
            Task DuplexDeadlineExceeded(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                callCount++;

                return Task.FromException(new RpcException(new Status(StatusCode.Unavailable, ""), new Metadata
                {
                    new Metadata.Entry("grpc-retry-pushback-ms", TimeSpan.FromSeconds(10).TotalMilliseconds.ToString())
                }));
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<DataMessage, DataMessage>(DuplexDeadlineExceeded);

            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(
                initialBackoff: TimeSpan.FromSeconds(10),
                maxBackoff: TimeSpan.FromSeconds(10),
                retryableStatusCodes: new List<StatusCode> { StatusCode.Unavailable });
            var channel = CreateChannel(serviceConfig: serviceConfig);

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.DuplexStreamingCall(new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(300)));

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

            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(retryableStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
            var channel = CreateChannel(serviceConfig: serviceConfig);

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow));

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            Assert.AreEqual(0, callCount);

            AssertHasLog(LogLevel.Debug, "RetryEvaluated", "Evaluated retry for failed gRPC call. Status code: 'DeadlineExceeded', Attempt: 1, Retry: False");
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

            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(retryableStatusCodes: new List<StatusCode> { StatusCode.DeadlineExceeded });
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

        [TestCase(1)]
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

            var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig(maxAttempts: 10, initialBackoff: TimeSpan.FromMilliseconds(hedgingDelay)));

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

        [TestCase(0, false)]
        [TestCase(GrpcChannel.DefaultMaxRetryBufferPerCallSize - 10, false)] // Final message size is bigger because of header + Protobuf field
        [TestCase(GrpcChannel.DefaultMaxRetryBufferPerCallSize + 10, true)]
        public async Task Unary_LargeMessages_ExceedPerCallBufferSize(long payloadSize, bool exceedBufferLimit)
        {
            var callCount = 0;
            Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
            {
                Interlocked.Increment(ref callCount);
                return Task.FromException<DataMessage>(new RpcException(new Status(StatusCode.Unavailable, "")));
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

            var channel = CreateChannel(serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig());

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
            }

            Assert.AreEqual(0, channel.CurrentRetryBufferSize);
        }

        [Test]
        public async Task Unary_MultipleLargeMessages_ExceedChannelMaxBufferSize()
        {
            // Arrange
            var sp1 = new SyncPoint(runContinuationsAsynchronously: true);
            var sp2 = new SyncPoint(runContinuationsAsynchronously: true);
            var sp3 = new SyncPoint(runContinuationsAsynchronously: true);
            var channel = CreateChannel(
                serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig(),
                maxRetryBufferSize: 200,
                maxRetryBufferPerCallSize: 100);

            var request = new DataMessage { Data = ByteString.CopyFrom(new byte[90]) };

            // Act
            var call1Task = MakeCall(Fixture, channel, request, sp1);
            await sp1.WaitForSyncPoint().DefaultTimeout();

            var call2Task = MakeCall(Fixture, channel, request, sp2);
            await sp2.WaitForSyncPoint().DefaultTimeout();

            // Will exceed channel buffer limit and won't retry
            var call3Task = MakeCall(Fixture, channel, request, sp3);
            await sp3.WaitForSyncPoint().DefaultTimeout();

            // Assert
            Assert.AreEqual(194, channel.CurrentRetryBufferSize);

            sp1.Continue();
            sp2.Continue();
            sp3.Continue();

            var response = await call1Task.DefaultTimeout();
            Assert.AreEqual(90, response.Data.Length);

            response = await call2Task.DefaultTimeout();
            Assert.AreEqual(90, response.Data.Length);

            // Can't retry because buffer size exceeded.
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call3Task).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);

            Assert.AreEqual(0, channel.CurrentRetryBufferSize);

            static Task<DataMessage> MakeCall(GrpcTestFixture<FunctionalTestsWebsite.Startup> fixture, GrpcChannel channel, DataMessage request, SyncPoint syncPoint)
            {
                var callCount = 0;
                async Task<DataMessage> UnaryFailure(DataMessage request, ServerCallContext context)
                {
                    Interlocked.Increment(ref callCount);
                    if (callCount == 1)
                    {
                        await syncPoint.WaitToContinue();
                        throw new RpcException(new Status(StatusCode.Unavailable, ""));
                    }
                    else
                    {
                        return request;
                    }
                }

                // Arrange
                var method = fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryFailure);

                var client = TestClientFactory.Create(channel, method);

                var call = client.UnaryCall(request);

                return call.ResponseAsync;
            }
        }

        [Test]
        public async Task ClientStreaming_MultipleWritesExceedPerCallLimit_Failure()
        {
            var nextFailure = 2;
            var callCount = 0;
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            async Task<DataMessage> ClientStreamingWithReadFailures(IAsyncStreamReader<DataMessage> requestStream, ServerCallContext context)
            {
                Interlocked.Increment(ref callCount);

                List<byte> bytes = new List<byte>();
                await foreach (var message in requestStream.ReadAllAsync())
                {
                    bytes.Add(message.Data[0]);

                    Logger.LogInformation($"Current count: {bytes.Count}, next failure: {nextFailure}.");

                    if (bytes.Count >= nextFailure)
                    {
                        await syncPoint.WaitToContinue();
                        throw new RpcException(new Status(StatusCode.Unavailable, ""));
                    }
                }

                return new DataMessage
                {
                    Data = ByteString.CopyFrom(bytes.ToArray())
                };
            }

            SetExpectedErrorsFilter(writeContext =>
            {
                return true;
            });

            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<DataMessage, DataMessage>(ClientStreamingWithReadFailures);
            var channel = CreateChannel(
                serviceConfig: ServiceConfigHelpers.CreateRetryServiceConfig(maxAttempts: 10),
                maxRetryAttempts: 10,
                maxRetryBufferPerCallSize: 100);
            var client = TestClientFactory.Create(channel, method);
            var sentData = new List<byte>();

            // Act
            var call = client.ClientStreamingCall();

            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();

            await syncPoint.WaitForSyncPoint();

            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();

            var s = syncPoint;
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            nextFailure = 15;
            s.Continue();

            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();

            Assert.AreEqual(96, channel.CurrentRetryBufferSize);

            await TestHelpers.AssertIsTrueRetryAsync(() => callCount == 2, "Wait for server to have second call.").DefaultTimeout();

            // This message exceeds the buffer size. Call is commited here.
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            Assert.AreEqual(0, channel.CurrentRetryBufferSize);

            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();

            await syncPoint.WaitForSyncPoint();

            await call.RequestStream.WriteAsync(new DataMessage { Data = ByteString.CopyFrom(new byte[] { 1 }) }).DefaultTimeout();

            s = syncPoint;
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            nextFailure = int.MaxValue;
            s.Continue();

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);

            Assert.AreEqual(2, callCount);
            Assert.AreEqual(0, channel.CurrentRetryBufferSize);
        }
    }
}
