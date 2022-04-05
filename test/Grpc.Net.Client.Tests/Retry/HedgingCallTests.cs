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

using System.Globalization;
using System.Net;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Internal.Retry;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Shared;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Retry
{
    [TestFixture]
    public class HedgingCallTests
    {
        [Test]
        public async Task Dispose_ActiveCalls_CleansUpActiveCalls()
        {
            // Arrange
            var allCallsOnServerTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var waitUntilFinishedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                // All calls are in-progress at once.
                Interlocked.Increment(ref callCount);
                if (callCount == 5)
                {
                    allCallsOnServerTcs.SetResult(null);
                }
                await waitUntilFinishedTcs.Task;

                await request.Content!.CopyToAsync(new MemoryStream());

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: TimeSpan.FromMilliseconds(20));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions());

            // Act
            hedgingCall.StartUnary(new HelloRequest { Name = "World" });
            Assert.IsFalse(hedgingCall.CreateHedgingCallsTask!.IsCompleted);

            // Assert
            Assert.AreEqual(1, hedgingCall._activeCalls.Count);

            await allCallsOnServerTcs.Task.DefaultTimeout();

            Assert.AreEqual(5, callCount);
            Assert.AreEqual(5, hedgingCall._activeCalls.Count);

            hedgingCall.Dispose();
            Assert.AreEqual(0, hedgingCall._activeCalls.Count);
            await hedgingCall.CreateHedgingCallsTask!.DefaultTimeout();

            waitUntilFinishedTcs.SetResult(null);
        }

        private HedgingPolicyInfo CreateHedgingPolicy(HedgingPolicy? hedgingPolicy) => GrpcMethodInfo.CreateHedgingPolicy(hedgingPolicy!);

        [Test]
        public async Task ActiveCalls_FatalStatusCode_CleansUpActiveCalls()
        {
            // Arrange
            var allCallsOnServerSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            var waitUntilFinishedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var callLock = new object();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                await request.Content!.CopyToAsync(new MemoryStream());

                // All calls are in-progress at once.
                bool allCallsOnServer = false;
                lock (callLock)
                {
                    callCount++;
                    if (callCount == 5)
                    {
                        allCallsOnServer = true;
                    }
                }
                if (allCallsOnServer)
                {
                    await allCallsOnServerSyncPoint.WaitToContinue();
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.InvalidArgument);
                }
                await waitUntilFinishedTcs.Task;

                throw new InvalidOperationException("Should never reach here.");
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: TimeSpan.FromMilliseconds(20));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions());

            // Act
            hedgingCall.StartUnary(new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(1, hedgingCall._activeCalls.Count);
            Assert.IsFalse(hedgingCall.CreateHedgingCallsTask!.IsCompleted);

            await allCallsOnServerSyncPoint.WaitForSyncPoint().DefaultTimeout();

            Assert.AreEqual(5, callCount);
            Assert.AreEqual(5, hedgingCall._activeCalls.Count);

            allCallsOnServerSyncPoint.Continue();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => hedgingCall.GetResponseAsync()).DefaultTimeout();
            Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);

            // Fatal status code will cancel other calls
            Assert.AreEqual(0, hedgingCall._activeCalls.Count);
            await hedgingCall.CreateHedgingCallsTask!.DefaultTimeout();
        }

        [Test]
        public async Task ClientStreamWriteAsync_NoActiveCalls_WaitsForNextCall()
        {
            // Arrange
            var allCallsOnServerSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            var callLock = new object();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
                _ = content.ReadAsStreamAsync();

                // All calls are in-progress at once.
                bool firstCallsOnServer = false;
                lock (callLock)
                {
                    callCount++;
                    if (callCount == 1)
                    {
                        firstCallsOnServer = true;
                    }
                }
                if (firstCallsOnServer)
                {
                    await allCallsOnServerSyncPoint.WaitToContinue();
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: "200");
                }

                await content.PushComplete.DefaultTimeout();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 5, hedgingDelay: TimeSpan.FromMilliseconds(200));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), new CallOptions());

            // Act
            hedgingCall.StartClientStreaming();
            await hedgingCall.ClientStreamWriter!.WriteAsync(new HelloRequest { Name = "Name 1" }).DefaultTimeout();

            // Assert
            Assert.AreEqual(1, hedgingCall._activeCalls.Count);
            Assert.IsFalse(hedgingCall.CreateHedgingCallsTask!.IsCompleted);

            await allCallsOnServerSyncPoint.WaitForSyncPoint().DefaultTimeout();
            allCallsOnServerSyncPoint.Continue();

            await TestHelpers.AssertIsTrueRetryAsync(() => hedgingCall._activeCalls.Count == 0, "Call should finish and then wait until next call.");

            // This call will wait until next hedging call starts
            await hedgingCall.ClientStreamWriter!.WriteAsync(new HelloRequest { Name = "Name 2" }).DefaultTimeout();
            Assert.AreEqual(1, hedgingCall._activeCalls.Count);

            await hedgingCall.ClientStreamWriter!.CompleteAsync().DefaultTimeout();

            var responseMessage = await hedgingCall.GetResponseAsync().DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            Assert.AreEqual(0, hedgingCall._activeCalls.Count);
            await hedgingCall.CreateHedgingCallsTask!.DefaultTimeout();
        }

        [Test]
        public async Task ResponseAsync_PushbackStop_SuccessAfterPushbackStop()
        {
            // Arrange
            var allCallsOnServerTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var returnSuccessTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                // All calls are in-progress at once.
                Interlocked.Increment(ref callCount);
                if (callCount == 2)
                {
                    allCallsOnServerTcs.TrySetResult(null);
                }
                await allCallsOnServerTcs.Task;

                await request.Content!.CopyToAsync(new MemoryStream());

                if (request.Headers.TryGetValues(GrpcProtocolConstants.RetryPreviousAttemptsHeader, out var headerValues) &&
                    headerValues.Single() == "1")
                {
                    await returnSuccessTcs.Task;

                    var reply = new HelloReply { Message = "Hello world" };
                    var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                    return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                }
                else
                {
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, customHeaders: new Dictionary<string, string>
                    {
                        [GrpcProtocolConstants.RetryPushbackHeader] = "-1"
                    });
                }
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(maxAttempts: 2);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions());

            // Act
            hedgingCall.StartUnary(new HelloRequest { Name = "World" });

            // Wait for both calls to be on the server
            await allCallsOnServerTcs.Task;

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => hedgingCall._activeCalls.Count == 1, "Wait for pushback to be returned.");
            returnSuccessTcs.SetResult(null);

            var rs = await hedgingCall.GetResponseAsync().DefaultTimeout();
            Assert.AreEqual("Hello world", rs.Message);
            Assert.AreEqual(StatusCode.OK, hedgingCall.GetStatus().StatusCode);
            Assert.AreEqual(2, callCount);
            Assert.AreEqual(0, hedgingCall._activeCalls.Count);
        }

        [Test]
        public async Task RetryThrottling_BecomesActiveDuringDelay_CancelFailure()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: "200");
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(
                hedgingDelay: TimeSpan.FromMilliseconds(200),
                retryThrottling: new RetryThrottlingPolicy
                {
                    MaxTokens = 5,
                    TokenRatio = 0.1
                });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions());

            // Act
            hedgingCall.StartUnary(new HelloRequest());

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => hedgingCall._activeCalls.Count == 0, "Wait for all calls to fail.").DefaultTimeout();

            CompatibilityHelpers.Assert(invoker.Channel.RetryThrottling != null);
            invoker.Channel.RetryThrottling.CallFailure();
            invoker.Channel.RetryThrottling.CallFailure();
            CompatibilityHelpers.Assert(invoker.Channel.RetryThrottling.IsRetryThrottlingActive());

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => hedgingCall.GetResponseAsync()).DefaultTimeout();
            Assert.AreEqual(1, callCount);
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual(StatusCode.Cancelled, hedgingCall.GetStatus().StatusCode);
            Assert.AreEqual("Retries stopped because retry throttling is active.", hedgingCall.GetStatus().Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_CancellationDuringBackoff_CanceledStatus()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            });
            var cts = new CancellationTokenSource();
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(hedgingDelay: TimeSpan.FromSeconds(10));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions(cancellationToken: cts.Token));

            // Act
            hedgingCall.StartUnary(new HelloRequest());
            Assert.IsNotNull(hedgingCall._ctsRegistration);

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => hedgingCall._activeCalls.Count == 0, "Wait for all calls to fail.").DefaultTimeout();

            cts.Cancel();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => hedgingCall.GetResponseAsync()).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("Call canceled by the client.", ex.Status.Detail);
            Assert.IsNull(hedgingCall._ctsRegistration);
        }

        [Test]
        public async Task AsyncUnaryCall_CancellationTokenSuccess_CleanedUp()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                await tcs.Task;

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var cts = new CancellationTokenSource();
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(hedgingDelay: TimeSpan.FromSeconds(10));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions(cancellationToken: cts.Token));

            // Act
            hedgingCall.StartUnary(new HelloRequest());
            Assert.IsNotNull(hedgingCall._ctsRegistration);
            tcs.SetResult(null);

            // Assert
            await hedgingCall.GetResponseAsync().DefaultTimeout();

            // There is a race between unregistering and GetResponseAsync returning.
            await TestHelpers.AssertIsTrueRetryAsync(() => hedgingCall._ctsRegistration == null, "Hedge call CTS unregistered.");
        }

        [Test]
        public async Task AsyncUnaryCall_DisposeDuringBackoff_CanceledStatus()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
            });
            var serviceConfig = ServiceConfigHelpers.CreateHedgingServiceConfig(hedgingDelay: TimeSpan.FromSeconds(10));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var hedgingCall = new HedgingCall<HelloRequest, HelloReply>(CreateHedgingPolicy(serviceConfig.MethodConfigs[0].HedgingPolicy), invoker.Channel, ClientTestHelpers.ServiceMethod, new CallOptions());

            // Act
            hedgingCall.StartUnary(new HelloRequest());

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() => hedgingCall._activeCalls.Count == 0, "Wait for all calls to fail.").DefaultTimeout();

            hedgingCall.Dispose();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => hedgingCall.GetResponseAsync()).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);
        }
    }
}
