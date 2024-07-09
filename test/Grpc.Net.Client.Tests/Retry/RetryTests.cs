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
using System.Net;
using Greet;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Retry;

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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
    public async Task AsyncUnaryCall_AuthInteceptor_Success()
    {
        // Arrange
        var testSink = new TestSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();
        var provider = services.BuildServiceProvider();

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var credentialsSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var credentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            await credentialsSyncPoint.WaitToContinue();
            metadata.Add("Authorization", $"Bearer TEST");
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: provider.GetRequiredService<ILoggerFactory>(), serviceConfig: serviceConfig, configure: options => options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials));

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

        await credentialsSyncPoint.WaitForSyncPoint().DefaultTimeout();
        credentialsSyncPoint.Continue();

        // Assert
        Assert.AreEqual("Hello world", (await call.ResponseAsync.DefaultTimeout()).Message);

        var write = testSink.Writes.Single(w => w.EventId.Name == "CallCommited");
        Assert.AreEqual("Call commited. Reason: ResponseHeadersReceived", write.State.ToString());
    }

    [Test]
    public async Task AsyncUnaryCall_AuthInteceptorDispose_Error()
    {
        // Arrange
        var testSink = new TestSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();
        var provider = services.BuildServiceProvider();

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var credentialsSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var credentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            var tcs = new TaskCompletionSource<bool>();
            context.CancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).SetResult(true), tcs);

            await Task.WhenAny(credentialsSyncPoint.WaitToContinue(), tcs.Task);
            metadata.Add("Authorization", $"Bearer TEST");
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: provider.GetRequiredService<ILoggerFactory>(), serviceConfig: serviceConfig, configure: options => options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials));

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });
        var responseTask = call.ResponseAsync;
        var responseHeadersTask = call.ResponseHeadersAsync;

        await credentialsSyncPoint.WaitForSyncPoint().DefaultTimeout();
        call.Dispose();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseHeadersTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);

        var write = testSink.Writes.Single(w => w.EventId.Name == "CallCommited");
        Assert.AreEqual("Call commited. Reason: Canceled", write.State.ToString());
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
                    customHeaders: new Dictionary<string, string> { ["call-count"] = callCount.ToString(CultureInfo.InvariantCulture) });
            }

            syncPoint.Continue();

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(
                HttpStatusCode.OK,
                streamContent,
                customHeaders: new Dictionary<string, string> { ["call-count"] = callCount.ToString(CultureInfo.InvariantCulture) });
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });
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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });
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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
            return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
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
    public async Task AsyncUnaryCall_CallDisposeDuringBackoff_CanceledStatus()
    {
        // Arrange
        var callCount = 0;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            callCount++;

            await request.Content!.CopyToAsync(new MemoryStream());
            return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });

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
        var call = invoker.AsyncClientStreamingCall();

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
        var call = invoker.AsyncClientStreamingCall();

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
        var call = invoker.AsyncClientStreamingCall();
        await call.RequestStream.CompleteAsync().DefaultTimeout();
        var resultTask = call.ResponseAsync;

        // Assert
        var writeException1 = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => call.RequestStream.WriteAsync(new HelloRequest { Name = "1" })).DefaultTimeout();
        Assert.AreEqual("Request stream has already been completed.", writeException1.Message);

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 1"
        }).DefaultTimeout()).DefaultTimeout();
        await streamContent.EndStreamAndWait();

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
        var call = invoker.AsyncClientStreamingCall();

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
        var call = invoker.AsyncClientStreamingCall();

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
        var call = invoker.AsyncClientStreamingCall();

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
    public async Task AsyncUnaryCall_Success_SuccussCommitLogged()
    {
        // Arrange
        var testSink = new TestSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();
        var provider = services.BuildServiceProvider();

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var content = request.Content!;
            await content.CopyToAsync(new MemoryStream());

            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: provider.GetRequiredService<ILoggerFactory>(), serviceConfig: serviceConfig);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        var log = testSink.Writes.Single(w => w.EventId.Name == "CallCommited");
        Assert.AreEqual("Call commited. Reason: ResponseHeadersReceived", log.State.ToString());
    }

    [Test]
    public async Task AsyncUnaryCall_NoMessagesSuccess_Failure()
    {
        // Arrange
        var testSink = new TestSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();
        var provider = services.BuildServiceProvider();

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var content = request.Content!;
            await content.CopyToAsync(new MemoryStream());

            return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.OK);
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: provider.GetRequiredService<ILoggerFactory>(), serviceConfig: serviceConfig);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" });
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        StringAssert.StartsWith("Failed to deserialize response message.", ex.Status.Detail);
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);

        var log = testSink.Writes.Single(w => w.EventId.Name == "CallCommited");
        Assert.AreEqual("Call commited. Reason: FatalStatusCode", log.State.ToString());
    }

    [Test]
    public async Task AsyncServerStreamingCall_NoMessagesSuccess_SuccussCommitLogged()
    {
        // Arrange
        var testSink = new TestSink();
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddProvider(new TestLoggerProvider(testSink));
        });
        services.AddNUnitLogger();
        var provider = services.BuildServiceProvider();

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var content = request.Content!;
            await content.CopyToAsync(new MemoryStream());

            return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.OK);
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: provider.GetRequiredService<ILoggerFactory>(), serviceConfig: serviceConfig);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest { Name = "World" });
        var moveNextTask = call.ResponseStream.MoveNext(CancellationToken.None);

        // Assert
        Assert.IsFalse(await moveNextTask);

        var log = testSink.Writes.Single(w => w.EventId.Name == "CallCommited");
        Assert.AreEqual("Call commited. Reason: ResponseHeadersReceived", log.State.ToString());
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
        var call = invoker.AsyncServerStreamingCall(new HelloRequest { Name = "World" });
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
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

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
        var call = invoker.AsyncDuplexStreamingCall();
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

    [Test]
    public void AsyncUnaryCall_DisposedChannel_Error()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK));
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);

        // Act & Assert
        invoker.Channel.Dispose();
        Assert.Throws<ObjectDisposedException>(() => invoker.AsyncUnaryCall(new HelloRequest { Name = "World" }));
    }

    [Test]
    public async Task AsyncUnaryCall_ChannelDisposeDuringBackoff_CanceledStatus()
    {
        // Arrange
        var callCount = 0;
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            callCount++;

            await request.Content!.CopyToAsync(new MemoryStream());
            return ResponseUtils.CreateHeadersOnlyResponse(HttpStatusCode.OK, StatusCode.Unavailable, retryPushbackHeader: TimeSpan.FromSeconds(10).TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
        });
        var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, serviceConfig: serviceConfig);
        var cts = new CancellationTokenSource();

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest { Name = "World" }, new CallOptions(cancellationToken: cts.Token));

        var delayTask = Task.Delay(100);
        var completedTask = await Task.WhenAny(call.ResponseAsync, delayTask);

        // Assert
        Assert.AreEqual(delayTask, completedTask); // Ensure that we're waiting for retry

        invoker.Channel.Dispose();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);
    }

    public enum ResponseHandleAction
    {
        ResponseAsync,
        ResponseHeadersAsync,
        Dispose,
        Nothing
    }

    public static object[] NoUnobservedExceptionsCases_Unary
    {
        get
        {
            var cases = new List<object[]>();
            AddCases(0, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.ResponseAsync);
            AddCases(0, addClientInterceptor: true, throwCancellationError: false, ResponseHandleAction.ResponseAsync);
            AddCases(0, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.ResponseHeadersAsync);
            AddCases(0, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.Dispose);
            AddCases(0, addClientInterceptor: true, throwCancellationError: false, ResponseHandleAction.Dispose);
            AddCases(1, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.Nothing);
            AddCases(0, addClientInterceptor: false, throwCancellationError: true, ResponseHandleAction.Nothing);
            return cases.ToArray();

            // Add a sync and async case for each.
            void AddCases(int expectedUnobservedExceptions, bool addClientInterceptor, bool throwCancellationError, ResponseHandleAction action)
            {
                cases.Add(new object[] { expectedUnobservedExceptions, true, addClientInterceptor, throwCancellationError, action });
                cases.Add(new object[] { expectedUnobservedExceptions, false, addClientInterceptor, throwCancellationError, action });
            }
        }
    }

    [TestCaseSource(nameof(NoUnobservedExceptionsCases_Unary))]
    public async Task AsyncUnaryCall_CallFailed_NoUnobservedExceptions(int expectedUnobservedExceptions, bool isAsync, bool addClientInterceptor, bool throwCancellationError, ResponseHandleAction action)
    {
        // Do this before running the test to clean up any pending unobserved exceptions from other tests.
        TriggerUnobservedExceptions();

        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(GetType());

        var unobservedExceptions = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobservedTaskException = (sender, e) =>
        {
            if (!e.Observed)
            {
                unobservedExceptions.Add(e.Exception!);
            }
        };

        TaskScheduler.UnobservedTaskException += onUnobservedTaskException;

        try
        {
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                if (isAsync)
                {
                    await Task.Delay(50);
                }
                if (throwCancellationError)
                {
                    throw new OperationCanceledException();
                }
                else
                {
                    throw new Exception("Test error");
                }
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            CallInvoker invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: loggerFactory, serviceConfig: serviceConfig);
            if (addClientInterceptor)
            {
                invoker = invoker.Intercept(new ClientLoggerInterceptor(loggerFactory));
            }

            // Act
            logger.LogDebug("Starting call");
            await MakeGrpcCallAsync(logger, invoker, action);

            logger.LogDebug("Waiting for finalizers");
            for (var i = 0; i < 5; i++)
            {
                TriggerUnobservedExceptions();
                await Task.Delay(10);
            }

            foreach (var exception in unobservedExceptions)
            {
                logger.LogCritical(exception, "Unobserved task exception");
            }

            // Assert
            Assert.AreEqual(expectedUnobservedExceptions, unobservedExceptions.Count);

            static async Task MakeGrpcCallAsync(ILogger logger, CallInvoker invoker, ResponseHandleAction action)
            {
                var runTask = Task.Run(async () =>
                {
                    var call = invoker.AsyncUnaryCall(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

                    switch (action)
                    {
                        case ResponseHandleAction.ResponseAsync:
                            await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
                            break;
                        case ResponseHandleAction.ResponseHeadersAsync:
                            await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync);
                            break;
                        case ResponseHandleAction.Dispose:
                            await WaitForCallCompleteAsync(logger, call);
                            call.Dispose();
                            break;
                        default:
                            // Do nothing (but wait until call is finished)
                            await WaitForCallCompleteAsync(logger, call);
                            break;
                    }
                });

                await runTask;
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobservedTaskException;
        }

        static async Task WaitForCallCompleteAsync(ILogger logger, AsyncUnaryCall<HelloReply> call)
        {
            await TestHelpers.AssertIsTrueRetryAsync(() =>
            {
                try
                {
                    call.GetStatus();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Wait for call to complete.", logger);
        }
    }

    public static object[] NoUnobservedExceptionsCases_Streaming
    {
        get
        {
            var cases = new List<object[]>();
            AddCases(0, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.ResponseHeadersAsync);
            AddCases(0, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.Dispose);
            AddCases(0, addClientInterceptor: true, throwCancellationError: false, ResponseHandleAction.Dispose);
            AddCases(0, addClientInterceptor: false, throwCancellationError: false, ResponseHandleAction.Nothing);
            AddCases(0, addClientInterceptor: false, throwCancellationError: true, ResponseHandleAction.Nothing);
            return cases.ToArray();

            // Add a sync and async case for each.
            void AddCases(int expectedUnobservedExceptions, bool addClientInterceptor, bool throwCancellationError, ResponseHandleAction action)
            {
                cases.Add(new object[] { expectedUnobservedExceptions, true, addClientInterceptor, throwCancellationError, action });
                cases.Add(new object[] { expectedUnobservedExceptions, false, addClientInterceptor, throwCancellationError, action });
            }
        }
    }

    [TestCaseSource(nameof(NoUnobservedExceptionsCases_Streaming))]
    public async Task AsyncDuplexStreamingCall_CallFailed_NoUnobservedExceptions(int expectedUnobservedExceptions, bool isAsync, bool addClientInterceptor, bool throwCancellationError, ResponseHandleAction action)
    {
        // Do this before running the test to clean up any pending unobserved exceptions from other tests.
        TriggerUnobservedExceptions();

        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(GetType());

        var unobservedExceptions = new List<Exception>();
        EventHandler<UnobservedTaskExceptionEventArgs> onUnobservedTaskException = (sender, e) =>
        {
            if (!e.Observed)
            {
                unobservedExceptions.Add(e.Exception!);
            }
        };

        TaskScheduler.UnobservedTaskException += onUnobservedTaskException;

        try
        {
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                if (isAsync)
                {
                    await Task.Delay(50);
                }
                if (throwCancellationError)
                {
                    throw new OperationCanceledException();
                }
                else
                {
                    throw new Exception("Test error");
                }
            });
            var serviceConfig = ServiceConfigHelpers.CreateRetryServiceConfig();
            CallInvoker invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory: loggerFactory, serviceConfig: serviceConfig);
            if (addClientInterceptor)
            {
                invoker = invoker.Intercept(new ClientLoggerInterceptor(loggerFactory));
            }

            // Act
            logger.LogDebug("Starting call");
            await MakeGrpcCallAsync(logger, invoker, action);

            logger.LogDebug("Waiting for finalizers");
            for (var i = 0; i < 5; i++)
            {
                TriggerUnobservedExceptions();
                await Task.Delay(10);
            }

            foreach (var exception in unobservedExceptions)
            {
                logger.LogCritical(exception, "Unobserved task exception");
            }

            // Assert
            Assert.AreEqual(expectedUnobservedExceptions, unobservedExceptions.Count);

            static async Task MakeGrpcCallAsync(ILogger logger, CallInvoker invoker, ResponseHandleAction action)
            {
                var runTask = Task.Run(async () =>
                {
                    var call = invoker.AsyncDuplexStreamingCall(ClientTestHelpers.GetServiceMethod(MethodType.DuplexStreaming), string.Empty, new CallOptions());

                    switch (action)
                    {
                        case ResponseHandleAction.ResponseHeadersAsync:
                            await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync);
                            break;
                        case ResponseHandleAction.Dispose:
                            await WaitForCallCompleteAsync(logger, call);
                            call.Dispose();
                            break;
                        default:
                            // Do nothing (but wait until call is finished)
                            await WaitForCallCompleteAsync(logger, call);
                            break;
                    }
                });

                await runTask;
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= onUnobservedTaskException;
        }

        static async Task WaitForCallCompleteAsync(ILogger logger, AsyncDuplexStreamingCall<HelloRequest, HelloReply> call)
        {
            await TestHelpers.AssertIsTrueRetryAsync(() =>
            {
                try
                {
                    call.GetStatus();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }, "Wait for call to complete.", logger);
        }
    }

    private static void TriggerUnobservedExceptions()
    {
        // Provoke the garbage collector to find the unobserved exception.
        GC.Collect();
        // Wait for any failed tasks to be garbage collected
        GC.WaitForPendingFinalizers();
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
