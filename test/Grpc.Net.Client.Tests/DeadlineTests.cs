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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class DeadlineTests
    {
        [Test]
        public async Task AsyncUnaryCall_SetSecondDeadline_RequestMessageContainsDeadlineHeader()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(
                httpClient,
                systemClock: new TestSystemClock(new DateTime(2019, 11, 29, 1, 1, 1, DateTimeKind.Utc)));

            // Act
            await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: invoker.Channel.Clock.UtcNow.AddSeconds(1)), new HelloRequest());

            // Assert
            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual("1S", httpRequestMessage!.Headers.GetValues(GrpcProtocolConstants.TimeoutHeader).Single());
        }

        [Test]
        public async Task AsyncUnaryCall_SetMaxValueDeadline_RequestMessageHasNoDeadlineHeader()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: DateTime.MaxValue), new HelloRequest());

            // Assert
            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual(0, httpRequestMessage!.Headers.Count(h => string.Equals(h.Key, GrpcProtocolConstants.TimeoutHeader, StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public async Task AsyncUnaryCall_StartPastDeadline_RequestMessageContainsMinDeadlineHeader()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(
                httpClient,
                systemClock: testSystemClock,
                loggerFactory: testLoggerFactory);

            // Act
            var responseTask = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: invoker.Channel.Clock.UtcNow.AddSeconds(-1)), new HelloRequest()).ResponseAsync;

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);

            // Ensure no HTTP request
            Assert.IsNull(httpRequestMessage);

            // Ensure deadline timer wasn't started
            Assert.IsFalse(testSink.Writes.Any(w => w.EventId.Name == "StartingDeadlineTimeout"));
        }

        [Test]
        public async Task AsyncUnaryCall_SetVeryLargeDeadline_MaximumDeadlineTimeoutSent()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var deadline = testSystemClock.UtcNow.AddDays(2000);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: testLoggerFactory, systemClock: testSystemClock);

            // Act
            await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: deadline), new HelloRequest());

            // Assert
            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual("99999999S", httpRequestMessage!.Headers.GetValues(GrpcProtocolConstants.TimeoutHeader).Single());

            var s = testSink.Writes.Single(w => w.EventId.Name == "DeadlineTimeoutTooLong");
            Assert.AreEqual("Deadline timeout 2000.00:00:00 is above maximum allowed timeout of 99999999 seconds. Maximum timeout will be used.", s.Message);
        }

        [Test]
        public async Task AsyncUnaryCall_SendDeadlineHeaderAndDeadlineValue_DeadlineValueIsUsed()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(
                httpClient,
                systemClock: new TestSystemClock(new DateTime(2019, 11, 29, 1, 1, 1, DateTimeKind.Utc)));

            var headers = new Metadata();
            headers.Add("grpc-timeout", "1D");

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(headers: headers, deadline: invoker.Channel.Clock.UtcNow.AddSeconds(1)), new HelloRequest());

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual("1S", httpRequestMessage!.Headers.GetValues(GrpcProtocolConstants.TimeoutHeader).Single());
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineDuringSend_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
                await content.PushComplete.DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK);
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock);
            var deadline = testSystemClock.UtcNow.AddSeconds(0.1);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: deadline));

            // Assert
            var responseTask = call.ResponseAsync;

            // Update time so deadline exceeds correctly
            testSystemClock.UtcNow = deadline;

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineDuringSend_ThrowOperationCanceledOnCancellation_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
                await content.PushComplete.DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK);
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock, configure: o => o.ThrowOperationCanceledOnCancellation = true);
            var deadline = testSystemClock.UtcNow.AddSeconds(0.1);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: deadline));

            // Assert
            var responseTask = call.ResponseAsync;

            // Update time so deadline exceeds correctly
            testSystemClock.UtcNow = deadline;

            await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineStatusResponse_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StringContent(string.Empty), StatusCode.DeadlineExceeded));
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock, disableClientDeadline: true);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: testSystemClock.UtcNow.AddSeconds(0.5)));

            // Assert
            var responseTask = call.ResponseAsync;

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineStatusResponse_ThrowOperationCanceledOnCancellation_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StringContent(string.Empty), StatusCode.DeadlineExceeded));
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock, configure: o => o.ThrowOperationCanceledOnCancellation = true, disableClientDeadline: true);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: testSystemClock.UtcNow.AddSeconds(0.5)));

            // Assert
            var responseTask = call.ResponseAsync;

            await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncServerStreamingCall_DeadlineStatusResponse_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StringContent(string.Empty), StatusCode.DeadlineExceeded));
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock, disableClientDeadline: true);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: testSystemClock.UtcNow.AddSeconds(0.5)), new HelloRequest());

            // Assert
            var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncServerStreamingCall_DeadlineStatusResponse_ThrowOperationCanceledOnCancellation_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StringContent(string.Empty), StatusCode.DeadlineExceeded));
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock, configure: o => o.ThrowOperationCanceledOnCancellation = true, disableClientDeadline: true);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: testSystemClock.UtcNow.AddSeconds(0.5)), new HelloRequest());

            // Assert
            var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

            await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => moveNextTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineBeforeWrite_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                await tcs.Task.DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK);
            });
            var systemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: systemClock);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: systemClock.UtcNow.AddMilliseconds(10)));

            // Ensure the deadline has passed
            systemClock.UtcNow = systemClock.UtcNow.AddMilliseconds(200);
            await Task.Delay(200);

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest())).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineDuringWrite_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                var stream = new SyncPointMemoryStream();
                var content = new StreamContent(stream);
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content, grpcStatusCode: null));
            });
            var systemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: systemClock);
            var deadline = systemClock.UtcNow.AddMilliseconds(0.1);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: deadline));

            systemClock.UtcNow = deadline;

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest())).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncServerStreamingCall_DeadlineDuringWrite_ResponseThrowsDeadlineExceededStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                var stream = new SyncPointMemoryStream();
                var content = new StreamContent(stream);
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: DateTime.UtcNow.AddSeconds(0.5)), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext(CancellationToken.None)).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_SuccessAndReadValuesAfterDeadline_ValuesReturned()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(
                httpClient,
                systemClock: new TestSystemClock(new DateTime(2019, 11, 29, 1, 1, 1, DateTimeKind.Utc)));

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: invoker.Channel.Clock.UtcNow.AddSeconds(0.5)), new HelloRequest());

            // Assert
            var result = await call.ResponseAsync.DefaultTimeout();
            Assert.IsNotNull(result);

            // Wait for deadline to trigger
            await Task.Delay(1000);

            Assert.IsNotNull(await call.ResponseHeadersAsync.DefaultTimeout());

            Assert.IsNotNull(call.GetTrailers());

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_SetNonUtcDeadline_ThrowError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: new DateTime(2000, DateTimeKind.Local)), new HelloRequest()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual("Deadline must have a kind DateTimeKind.Utc or be equal to DateTime.MaxValue or DateTime.MinValue.", ex.Message);
        }

        [Test]
        public async Task AsyncClientStreamingCall_DeadlineLargerThanMaxTimerDueTime_DeadlineExceeded()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
                await content.PushComplete.DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK);
            });
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, systemClock: testSystemClock, maxTimerPeriod: 20, loggerFactory: testLoggerFactory);
            var timeout = TimeSpan.FromSeconds(0.2);
            var deadline = testSystemClock.UtcNow.Add(timeout);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(deadline: deadline));

            // Assert
            var responseTask = call.ResponseAsync;

            await Task.Delay(timeout);

            // Update time so deadline exceeds correctly
            testSystemClock.UtcNow = deadline;

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);

            var write = testSink.Writes.First(w => w.EventId.Name == "DeadlineTimerRescheduled");
            Assert.AreEqual(LogLevel.Trace, write.LogLevel);
            Assert.AreEqual("Deadline timer triggered but 00:00:00.2000000 remaining before deadline exceeded. Deadline timer rescheduled.", write.Message);
        }

        private class TestSystemClock : ISystemClock
        {
            public TestSystemClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; set; }
        }
    }
}
