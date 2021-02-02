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
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Retry
{
    [TestFixture]
    public class RetryTests
    {
        [Test]
        public async Task AsyncUnaryCall_SuccessAfterRetry_RequestContentSent()
        {
            // Arrange
            HttpContent? content = null;

            bool? firstRequestPreviousAttemptsHeader = null;
            string? secondRequestPreviousAttemptsHeaderValue = null;
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;

                content = request.Content!;
                await content.CopyToAsync(requestContent);
                requestContent.Seek(0, SeekOrigin.Begin);

                if (callCount == 1)
                {
                    firstRequestPreviousAttemptsHeader = request.Headers.TryGetValues(GrpcProtocolConstants.RetryPreviousAttemptsHeader, out _);

                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                if (request.Headers.TryGetValues(GrpcProtocolConstants.RetryPreviousAttemptsHeader, out var retryAttemptCountValue))
                {
                    secondRequestPreviousAttemptsHeaderValue = retryAttemptCountValue.Single();
                }

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, customTrailers: new Dictionary<string, string>
                {
                    ["custom-trailer"] = "Value!"
                });
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(2, callCount);
            Assert.AreEqual("Hello world", (await call.ResponseAsync.DefaultTimeout()).Message);
            Assert.AreEqual("1", (await call.ResponseHeadersAsync.DefaultTimeout()).GetValue(GrpcProtocolConstants.RetryPreviousAttemptsHeader));

            Assert.IsNotNull(content);

            var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();

            Assert.AreEqual("World", requestMessage!.Name);

            Assert.IsFalse(firstRequestPreviousAttemptsHeader);
            Assert.AreEqual("1", secondRequestPreviousAttemptsHeaderValue);

            var trailers = call.GetTrailers();
            Assert.AreEqual("Value!", trailers.GetValue("custom-trailer"));
        }

        [Test]
        public async Task AsyncUnaryCall_SuccessAfterRetry_AccessResponseHeaders_SuccessfullyResponseHeadersReturned()
        {
            // Arrange
            HttpContent? content = null;
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                content = request.Content!;

                if (callCount == 1)
                {
                    await content.CopyToAsync(new MemoryStream());

                    await syncPoint.WaitForSyncPoint();

                    return ResponseUtils.CreateHeadersOnlyResponse(
                        HttpStatusCode.OK,
                        StatusCode.Unavailable,
                        customHeaders: new Dictionary<string, string> { ["call-count"] = callCount.ToString() });
                }

                syncPoint.Continue();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(
                    HttpStatusCode.OK,
                    streamContent,
                    customHeaders: new Dictionary<string, string> { ["call-count"] = callCount.ToString() });
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
            var headersTask = call.ResponseHeadersAsync;

            // Wait until the first call has failed and the second is on the server
            await syncPoint.WaitToContinue().DefaultTimeout();

            // Assert
            Assert.AreEqual(2, callCount);
            Assert.AreEqual("Hello world", (await call.ResponseAsync.DefaultTimeout()).Message);

            var headers = await headersTask.DefaultTimeout();
            Assert.AreEqual("2", headers.GetValue("call-count"));
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedRetryAttempts_Failure()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(maxAttempts: 3);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(3, callCount);
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_FailureWithLongDelay_Dispose_CallImmediatelyDisposed()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
            });
            // Very long delay
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(initialBackoff: TimeSpan.FromSeconds(30), maxBackoff: TimeSpan.FromSeconds(30));
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
            var resultTask = call.ResponseAsync;

            // Test will timeout if dispose doesn't kill the timer.
            call.Dispose();

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => resultTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);
        }

        [TestCase("")]
        [TestCase("-1")]
        [TestCase("stop")]
        public async Task AsyncUnaryCall_PushbackStop_Failure(string header)
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, new StringContent(""), StatusCode.Unavailable, retryPushbackHeader: header);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(1, callCount);
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
            Assert.AreEqual(StatusCode.Unavailable, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_PushbackExpicitDelay_DelayForSpecifiedDuration()
        {
            // Arrange
            Task? delayTask = null;
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                if (callCount == 1)
                {
                    await request.Content!.CopyToAsync(new MemoryStream());
                    delayTask = Task.Delay(100);
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: "200");
                }

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(backoffMultiplier: 1);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Delay of 100ms will finish before second record which has a pushback delay of 200ms
            var completedTask = await Task.WhenAny(call.ResponseAsync, delayTask!).DefaultTimeout();
            var rs = await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(delayTask, completedTask); // Response task should finish after
            Assert.AreEqual(2, callCount);
            Assert.AreEqual("Hello world", rs.Message);
        }

        [Test]
        public async Task AsyncUnaryCall_CancellationDuringBackoff_CanceledStatus()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString());
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var cts = new CancellationTokenSource();

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(cancellationToken: cts.Token), new HelloRequest { Name = "World" });

            var delayTask = Task.Delay(100);
            var completedTask = await Task.WhenAny(call.ResponseAsync, delayTask);

            // Assert
            Assert.AreEqual(delayTask, completedTask); // Ensure that we're waiting for retry

            cts.Cancel();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("Call canceled by the client.", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_DisposeDuringBackoff_CanceledStatus()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;

                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString());
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
            var cts = new CancellationTokenSource();

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(cancellationToken: cts.Token), new HelloRequest { Name = "World" });

            var delayTask = Task.Delay(100);
            var completedTask = await Task.WhenAny(call.ResponseAsync, delayTask);

            // Assert
            Assert.AreEqual(delayTask, completedTask); // Ensure that we're waiting for retry

            call.Dispose();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_PushbackExplicitDelayExceedAttempts_Failure()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: "0");
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig(maxAttempts: 5);
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(5, callCount);
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_UnsupportedStatusCode_Failure()
        {
            // Arrange
            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                await request.Content!.CopyToAsync(new MemoryStream());
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, new StringContent(""), StatusCode.InvalidArgument);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(1, callCount);
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
            Assert.AreEqual(StatusCode.InvalidArgument, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_Success_RequestContentSent()
        {
            // Arrange
            HttpContent? content = null;

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                content = request.Content;

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

            // Assert
            Assert.AreEqual(1, callCount);
            Assert.AreEqual("Hello world", (await call.ResponseAsync.DefaultTimeout()).Message);
        }

        [Test]
        public async Task AsyncClientStreamingCall_SuccessAfterRetry_RequestContentSent()
        {
            // Arrange
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                var currentContent = new MemoryStream();
                await request.Content!.CopyToAsync(currentContent);

                if (callCount == 1)
                {
                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                currentContent.Seek(0, SeekOrigin.Begin);
                await currentContent.CopyToAsync(requestContent);

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            Assert.IsNotNull(call);

            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var responseMessage = await responseTask.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            requestContent.Seek(0, SeekOrigin.Begin);

            var requests = new List<HelloRequest>();
            while (true)
            {
                var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
                if (requestMessage == null)
                {
                    break;
                }

                requests.Add(requestMessage);
            }

            Assert.AreEqual(2, requests.Count);
            Assert.AreEqual("1", requests[0].Name);
            Assert.AreEqual("2", requests[1].Name);

            call.Dispose();
        }

        [Test]
        public async Task ClientStreamWriter_WriteWhilePendingWrite_ErrorThrown()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                var streamContent = new StreamContent(new SyncPointMemoryStream());
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent));
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            var writeTask1 = call.RequestStream.WriteAsync(new HelloRequest { Name = "1" });
            Assert.IsFalse(writeTask1.IsCompleted);

            var writeTask2 = call.RequestStream.WriteAsync(new HelloRequest { Name = "2" });
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => writeTask2).DefaultTimeout();

            Assert.AreEqual("Can't write the message because the previous write is in progress.", ex.Message);
        }

        [Test]
        public async Task ClientStreamWriter_WriteWhileComplete_ErrorThrown()
        {
            // Arrange
            var streamContent = new SyncPointMemoryStream();
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());
            await call.RequestStream.CompleteAsync().DefaultTimeout();
            var resultTask = call.ResponseAsync;

            // Assert
            var writeException1 = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
            Assert.AreEqual("Request stream has already been completed.", writeException1.Message);

            await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
            {
                Message = "Hello world 1"
            }).DefaultTimeout()).DefaultTimeout();
            await streamContent.AddDataAndWait(new byte[0]);

            var result = await resultTask.DefaultTimeout();
            Assert.AreEqual("Hello world 1", result.Message);

            var writeException2 = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "2" })).DefaultTimeout();
            Assert.AreEqual("Request stream has already been completed.", writeException2.Message);
        }

        [Test]
        public async Task AsyncClientStreamingCall_CompleteAndWriteAfterResult_Error()
        {
            // Arrange
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                _ = request.Content!.ReadAsStreamAsync();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            var responseMessage = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            requestContent.Seek(0, SeekOrigin.Begin);

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
            Assert.AreEqual("Request stream has already been completed.", ex.Message);
        }

        [Test]
        public async Task AsyncClientStreamingCall_WriteAfterResult_Error()
        {
            // Arrange
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                Interlocked.Increment(ref callCount);

                _ = request.Content!.ReadAsStreamAsync();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            var responseMessage = await call.ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
            Assert.AreEqual(StatusCode.OK, ex.StatusCode);
        }

        [Test]
        public async Task AsyncClientStreamingCall_OneMessageSentThenRetryThenAnotherMessage_RequestContentSent()
        {
            // Arrange
            var requestContent = new MemoryStream();
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;

                if (callCount == 1)
                {
                    _ = content.CopyToAsync(new MemoryStream());

                    await syncPoint.WaitForSyncPoint();

                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                syncPoint.Continue();

                await content.PushComplete.DefaultTimeout();
                await content.CopyToAsync(requestContent);
                requestContent.Seek(0, SeekOrigin.Begin);

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ClientStreaming), string.Empty, new CallOptions());

            // Assert
            Assert.IsNotNull(call);

            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();

            // Wait until the first call has failed and the second is on the server
            await syncPoint.WaitToContinue().DefaultTimeout();

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            var responseMessage = await responseTask.DefaultTimeout();
            Assert.AreEqual("Hello world", responseMessage.Message);

            var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.AreEqual("1", requestMessage!.Name);
            requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.AreEqual("2", requestMessage!.Name);
            requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.IsNull(requestMessage);
        }

        [Test]
        public async Task AsyncServerStreamingCall_SuccessAfterRetry_RequestContentSent()
        {
            // Arrange
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            var requestContent = new MemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;

                var content = request.Content!;
                await content.CopyToAsync(requestContent);
                requestContent.Seek(0, SeekOrigin.Begin);

                if (callCount == 1)
                {
                    await syncPoint.WaitForSyncPoint();

                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                syncPoint.Continue();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.ServerStreaming), string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
            var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

            // Wait until the first call has failed and the second is on the server
            await syncPoint.WaitToContinue().DefaultTimeout();

            // Assert
            Assert.IsTrue(await moveNextTask);
            Assert.AreEqual("Hello world", call.ResponseStream.Current.Message);

            var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.AreEqual("World", requestMessage!.Name);
            requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.IsNull(requestMessage);
        }

        [Test]
        public async Task AsyncServerStreamingCall_FailureAfterReadingResponseMessage_Failure()
        {
            // Arrange
            var streamContent = new SyncPointMemoryStream();

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(request =>
            {
                callCount++;
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent)));
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncServerStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            var responseStream = call.ResponseStream;

            // Assert
            Assert.IsNull(responseStream.Current);

            var moveNextTask1 = responseStream.MoveNext(CancellationToken.None);
            Assert.IsFalse(moveNextTask1.IsCompleted);

            await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
            {
                Message = "Hello world 1"
            }).DefaultTimeout()).DefaultTimeout();

            Assert.IsTrue(await moveNextTask1.DefaultTimeout());
            Assert.IsNotNull(responseStream.Current);
            Assert.AreEqual("Hello world 1", responseStream.Current.Message);

            var moveNextTask2 = responseStream.MoveNext(CancellationToken.None);
            Assert.IsFalse(moveNextTask2.IsCompleted);

            await streamContent.AddExceptionAndWait(new Exception("Exception!")).DefaultTimeout();

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => moveNextTask2).DefaultTimeout();
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
            Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
            Assert.AreEqual("Error reading next message. Exception: Exception!", call.GetStatus().Detail);
        }

        [Test]
        public async Task AsyncDuplexStreamingCall_SuccessAfterRetry_RequestContentSent()
        {
            // Arrange
            var requestContent = new MemoryStream();
            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            var callCount = 0;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                callCount++;
                var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;

                if (callCount == 1)
                {
                    _ = content.CopyToAsync(new MemoryStream());

                    await syncPoint.WaitForSyncPoint();

                    return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable);
                }

                syncPoint.Continue();

                await content.PushComplete.DefaultTimeout();
                await content.CopyToAsync(requestContent);
                requestContent.Seek(0, SeekOrigin.Begin);

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

            // Act
            var call = invoker.AsyncDuplexStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.GetServiceMethod(MethodType.DuplexStreaming), string.Empty, new CallOptions());
            var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();

            // Wait until the first call has failed and the second is on the server
            await syncPoint.WaitToContinue().DefaultTimeout();

            await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

            await call.RequestStream.CompleteAsync().DefaultTimeout();

            // Assert
            Assert.IsTrue(await moveNextTask.DefaultTimeout());
            Assert.AreEqual("Hello world", call.ResponseStream.Current.Message);

            var requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.AreEqual("1", requestMessage!.Name);
            requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.AreEqual("2", requestMessage!.Name);
            requestMessage = await ReadRequestMessage(requestContent).DefaultTimeout();
            Assert.IsNull(requestMessage);
        }

        private static Task<HelloRequest?> ReadRequestMessage(Stream requestContent)
        {
            return StreamSerializationHelper.ReadMessageAsync(
                requestContent,
                ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                GrpcProtocolConstants.IdentityGrpcEncoding,
                maximumMessageSize: null,
                GrpcProtocolConstants.DefaultCompressionProviders,
                singleMessage: false,
                CancellationToken.None);
        }
    }
}
