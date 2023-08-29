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

using System.Net;
using System.Net.Http.Headers;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class GetTrailersTests
{
    [Test]
    public async Task AsyncUnaryCall_MessageReturned_ReturnsTrailers()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            response.TrailingHeaders().Add("custom-header", "value");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var message = await call;
        var trailers1 = call.GetTrailers();
        var trailers2 = call.GetTrailers();

        // Assert
        Assert.AreSame(trailers1, trailers2);
        Assert.AreEqual("value", trailers1.GetValue("custom-header"));
    }

    [Test]
    public async Task AsyncUnaryCall_HeadersReturned_ReturnsTrailers()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            response.TrailingHeaders().Add("custom-header", "value");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();
        var trailers = call.GetTrailers();

        // Assert
        Assert.AreEqual("value", trailers.GetValue("custom-header"));
    }

    [Test]
    public void AsyncUnaryCall_UnfinishedCall_ThrowsError()
    {
        // Arrange
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            await tcs.Task;
            return ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        // Assert
        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);

        tcs.TrySetResult(null);
    }

    [Test]
    public void AsyncUnaryCall_ErrorCall_ThrowsError()
    {
        // Arrange
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new Exception("An error!"));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        // Assert
        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);
    }

    [Test]
    public async Task AsyncUnaryCall_ErrorParsingTrailers_ThrowsError()
    {
        // Arrange
        var testSink = new TestSink();
        var loggerFactory = new TestLoggerFactory(testSink, true);

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(
                HttpStatusCode.OK,
                streamContent,
                grpcStatusCode: StatusCode.Aborted,
                customTrailers: new Dictionary<string, string>
                {
                    ["blah-bin"] = "!"
                });
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Aborted, ex.StatusCode);
        Assert.AreEqual(string.Empty, ex.Status.Detail);
        Assert.AreEqual(0, ex.Trailers.Count);

        var log = testSink.Writes.Single(w => w.EventId.Name == "ErrorParsingTrailers");
        Assert.AreEqual("Error parsing trailers.", log.State.ToString());
        Assert.AreEqual("Invalid Base-64 header value.", log.Exception.Message);
    }

#if NET462
    [Test]
    public async Task AsyncUnaryCall_NoTrailers_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.RequestMessage.Properties.Remove("__ResponseTrailers");
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("No grpc-status found on response.", ex.Status.Detail);
        Assert.AreEqual(0, ex.Trailers.Count);
    }

    [Test]
    public async Task AsyncUnaryCall_BadTrailersType_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.RequestMessage.Properties["__ResponseTrailers"] = new object();
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("No grpc-status found on response.", ex.Status.Detail);
        Assert.AreEqual(0, ex.Trailers.Count);
    }

    [Test]
    public async Task AsyncUnaryCall_NoTrailers_WinHttpHandler_ThrowsError()
    {
        // Arrange
        var httpMessageHandler = TestHttpMessageHandler.Create(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.RequestMessage.Properties.Remove("__ResponseTrailers");
            response.Headers.Add("custom", "ABC");
            return response;
        });
#pragma warning disable CS0436 // Using custom WinHttpHandler type rather than the real one
        var invoker = HttpClientCallInvokerFactory.Create(new WinHttpHandler(httpMessageHandler), "https://localhost");
#pragma warning restore CS0436

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("No grpc-status found on response. Using gRPC with WinHttp has Windows and package version requirements. See https://aka.ms/aspnet/grpc/netstandard for details.", ex.Status.Detail);
        Assert.AreEqual(0, ex.Trailers.Count);
    }
#endif

    [Test]
    public void AsyncClientStreamingCall_UnfinishedCall_ThrowsError()
    {
        // Arrange
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            await tcs.Task;
            return ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        // Assert
        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);

        tcs.TrySetResult(null);
    }

    [Test]
    public void AsyncServerStreamingCall_UnfinishedCall_ThrowsError()
    {
        // Arrange
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            await tcs.Task;
            return ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        // Assert
        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);

        tcs.TrySetResult(null);
    }

    [Test]
    public async Task AsyncServerStreamingCall_UnfinishedReader_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponsesContent(
                new HelloReply
                {
                    Message = "Hello world 1"
                },
                new HelloReply
                {
                    Message = "Hello world 2"
                }).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var responseStream = call.ResponseStream;

        Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        // Assert
        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);
    }

    [Test]
    public async Task AsyncServerStreamingCall_FinishedReader_ReturnsTrailers()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponsesContent(
                new HelloReply
                {
                    Message = "Hello world 1"
                },
                new HelloReply
                {
                    Message = "Hello world 2"
                }).DefaultTimeout();

            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.TrailingHeaders().Add("custom-header", "value");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var responseStream = call.ResponseStream;

        Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        Assert.IsTrue(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        var trailers = call.GetTrailers();

        // Assert
        Assert.AreEqual("value", trailers.GetValue("custom-header"));
    }

    [Test]
    public async Task AsyncClientStreamingCall_CompleteWriter_ReturnsTrailers()
    {
        // Arrange
        var trailingHeadersWrittenTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = TestHttpMessageHandler.Create(request =>
        {
            var content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
            var stream = new SyncPointMemoryStream();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream));

            _ = Task.Run(async () =>
            {
                // Add a response message after the client has completed
                await content.PushComplete.DefaultTimeout();

                var messageData = await ClientTestHelpers.GetResponseDataAsync(new HelloReply { Message = "Hello world" }).DefaultTimeout();
                await stream.AddDataAndWait(messageData).DefaultTimeout();
                await stream.EndStreamAndWait().DefaultTimeout();

                response.TrailingHeaders().Add("custom-header", "value");
                trailingHeadersWrittenTcs.SetResult(true);
            });

            return Task.FromResult(response);
        });

        var invoker = HttpClientCallInvokerFactory.Create(handler, "https://localhost");

        // Act
        var call = invoker.AsyncClientStreamingCall();
        await call.RequestStream.CompleteAsync().DefaultTimeout();
        await Task.WhenAll(call.ResponseAsync, trailingHeadersWrittenTcs.Task).DefaultTimeout();
        var trailers = call.GetTrailers();

        // Assert
        Assert.AreEqual("value", trailers.GetValue("custom-header"));
    }

    [Test]
    public void AsyncClientStreamingCall_UncompleteWriter_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var stream = new SyncPointMemoryStream();

            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream), grpcStatusCode: null);
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        // Assert
        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);
    }

    [Test]
    public void AsyncClientStreamingCall_NotFoundStatus_ReturnEmpty()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var trailers = call.GetTrailers();

        // Assert
        Assert.AreEqual(0, trailers.Count);
    }

    [Test]
    public void AsyncClientStreamingCall_InvalidContentType_ReturnEmpty()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK);
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var trailers = call.GetTrailers();

        // Assert
        Assert.AreEqual(0, trailers.Count);
    }
}
