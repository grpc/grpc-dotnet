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
using System.Diagnostics.Tracing;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class EventSourceTests : FunctionalTestBase
    {
        private static Dictionary<string, string?> EnableCountersArgs =
            new Dictionary<string, string?>
            {
                ["EventCounterIntervalSec"] = "0.1"
            };

        [SetUp]
        public void Reset()
        {
            Grpc.Net.Client.Internal.GrpcEventSource.Log.ResetCounters();
            Grpc.AspNetCore.Server.Internal.GrpcEventSource.Log.ResetCounters();
        }

        [Test]
        public async Task UnaryMethod_SuccessfulCall_PollingCountersUpdatedCorrectly()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task<HelloReply> UnarySuccess(HelloRequest request, ServerCallContext context)
            {
                await tcs.Task.DefaultTimeout();

                return new HelloReply();
            }

            // Arrange
            var clientEventListener = CreateEnableListener(Grpc.Net.Client.Internal.GrpcEventSource.Log);
            var serverEventListener = CreateEnableListener(Grpc.AspNetCore.Server.Internal.GrpcEventSource.Log);

            // Act - Start call
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnarySuccess);

            var client = TestClientFactory.Create(Channel, method);

            var call = client.UnaryCall(new HelloRequest());

            // Assert - Call in progress
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["total-calls"] = 1,
                ["current-calls"] = 1,
                ["messages-sent"] = 0,
                ["messages-received"] = 1,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["total-calls"] = 1,
                ["current-calls"] = 1,
                ["messages-sent"] = 1,
                ["messages-received"] = 0,
            }).DefaultTimeout();

            // Act - Complete call
            tcs.SetResult(true);

            await call.ResponseAsync.DefaultTimeout();

            // Assert - Call complete
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["total-calls"] = 1,
                ["current-calls"] = 0,
                ["messages-sent"] = 1,
                ["messages-received"] = 1,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["total-calls"] = 1,
                ["current-calls"] = 0,
                ["messages-sent"] = 1,
                ["messages-received"] = 1,
            }).DefaultTimeout();
        }

        [Test]
        public async Task UnaryMethod_ErrorCall_PollingCountersUpdatedCorrectly()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Ignore errors
            SetExpectedErrorsFilter(writeContext =>
            {
                return true;
            });

            async Task<HelloReply> UnaryError(HelloRequest request, ServerCallContext context)
            {
                await tcs.Task.DefaultTimeout();

                throw new Exception("Error!");
            }

            // Arrange
            var clientEventListener = CreateEnableListener(Grpc.Net.Client.Internal.GrpcEventSource.Log);
            var serverEventListener = CreateEnableListener(Grpc.AspNetCore.Server.Internal.GrpcEventSource.Log);

            // Act - Start call
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryError);

            var client = TestClientFactory.Create(Channel, method);

            var call = client.UnaryCall(new HelloRequest());

            // Assert - Call in progress
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 1,
                ["calls-failed"] = 0,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 1,
                ["calls-failed"] = 0,
            }).DefaultTimeout();

            // Act - Complete call
            tcs.SetResult(true);

            await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync.DefaultTimeout()).DefaultTimeout();

            // Assert - Call complete
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 0,
                ["calls-failed"] = 1,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 0,
                ["calls-failed"] = 1,
            }).DefaultTimeout();
        }

        [Test]
        public async Task UnaryMethod_DeadlineExceededCall_PollingCountersUpdatedCorrectly()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Ignore errors
            SetExpectedErrorsFilter(writeContext =>
            {
                return true;
            });

            async Task<HelloReply> UnaryDeadlineExceeded(HelloRequest request, ServerCallContext context)
            {
                await PollAssert(() => context.Status.StatusCode == StatusCode.DeadlineExceeded).DefaultTimeout();

                tcs.TrySetResult(true);

                return new HelloReply();
            }

            // Arrange
            var clientEventListener = CreateEnableListener(Grpc.Net.Client.Internal.GrpcEventSource.Log);
            var serverEventListener = CreateEnableListener(Grpc.AspNetCore.Server.Internal.GrpcEventSource.Log);

            // Act - Start call
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryDeadlineExceeded);

            var channel = CreateChannel();
            channel.DisableClientDeadlineTimer = true;

            var client = TestClientFactory.Create(Channel, method);

            var call = client.UnaryCall(new HelloRequest(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(200)));

            // Assert - Call in progress
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 1,
                ["calls-failed"] = 0,
                ["calls-deadline-exceeded"] = 0,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 1,
                ["calls-failed"] = 0,
                ["calls-deadline-exceeded"] = 0,
            }).DefaultTimeout();

            // Act - Wait for call to deadline on server
            await tcs.Task.DefaultTimeout();

            // Assert - Call complete
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 0,
                ["calls-failed"] = 1,
                ["calls-deadline-exceeded"] = 1,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 0,
                ["calls-failed"] = 1,
                ["calls-deadline-exceeded"] = 1,
            }).DefaultTimeout();
        }

        [Test]
        public async Task DuplexStreamingMethod_Success_PollingCountersUpdatedCorrectly()
        {
            async Task DuplexStreamingMethod(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                while (await requestStream.MoveNext().DefaultTimeout())
                {

                }

                await responseStream.WriteAsync(new HelloReply { Message = "Message 1" }).DefaultTimeout();
                await responseStream.WriteAsync(new HelloReply { Message = "Message 2" }).DefaultTimeout();
            }

            // Arrange
            var clientEventListener = CreateEnableListener(Grpc.Net.Client.Internal.GrpcEventSource.Log);
            var serverEventListener = CreateEnableListener(Grpc.AspNetCore.Server.Internal.GrpcEventSource.Log);

            // Act - Start call
            var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<HelloRequest, HelloReply>(DuplexStreamingMethod);

            var client = TestClientFactory.Create(Channel, method);

            var call = client.DuplexStreamingCall();

            // Assert - Call in progress
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 1,
                ["messages-sent"] = 0,
                ["messages-received"] = 0,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 1,
                ["messages-sent"] = 0,
                ["messages-received"] = 0,
            }).DefaultTimeout();

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "Name 1" }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "Name 2" }).DefaultTimeout();
            await call.RequestStream.CompleteAsync().DefaultTimeout();

            while (await call.ResponseStream.MoveNext().DefaultTimeout())
            {
            }

            // Assert - Call complete
            await AssertCounters(serverEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 0,
                ["messages-sent"] = 2,
                ["messages-received"] = 2,
            }).DefaultTimeout();
            await AssertCounters(clientEventListener, new Dictionary<string, long>
            {
                ["current-calls"] = 0,
                ["messages-sent"] = 2,
                ["messages-received"] = 2,
            }).DefaultTimeout();
        }

        private async Task PollAssert(Func<bool> predicate)
        {
            while (true)
            {
                if (predicate())
                {
                    return;
                }

                await Task.Delay(100);
            }
        }

        private async Task AssertCounters(TestEventListener listener, IDictionary<string, long> expectedValues)
        {
            var subscriptions = new List<ListenerSubscription>();
            foreach (var expectedValue in expectedValues)
            {
                subscriptions.Add(listener.Subscribe(expectedValue.Key, expectedValue.Value));
            }

            var tasks = new List<Task>();
            foreach (var subscription in subscriptions)
            {
                var t = Task.Run(async () =>
                {
                    var cts = new CancellationTokenSource();
                    if (subscription.Task == await Task.WhenAny(subscription.Task, Task.Delay(TimeSpan.FromSeconds(2), cts.Token)))
                    {
                        cts.Cancel();
                        await subscription.Task.DefaultTimeout();
                    }
                    else
                    {
                        throw new Exception(@$"Did not get ""{subscription.CounterName}"" = {subscription.ExpectedValue} in the allowed time.");
                    }
                });
                tasks.Add(t);
            }

            await Task.WhenAll(tasks).DefaultTimeout();
        }

        private TestEventListener CreateEnableListener(EventSource eventSource)
        {
            var listener = new TestEventListener(-1);
            listener.EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All, EnableCountersArgs);
            return listener;
        }
    }
}
