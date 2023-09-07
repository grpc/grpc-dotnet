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
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class ResponseAsyncTests
{
    [Test]
    public async Task AsyncUnaryCall_AwaitMultipleTimes_SameMessageReturned()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });

        var response1 = await call;
        var response2 = await call;
        var response3 = await call.ResponseAsync.DefaultTimeout();
        var response4 = await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Hello world", response1.Message);

        Assert.AreEqual(response1, response2);
        Assert.AreEqual(response1, response3);
        Assert.AreEqual(response1, response4);
    }

    [Test]
    public async Task AsyncUnaryCall_DisposeAfterHeadersAndBeforeMessage_ThrowsError()
    {
        // Arrange
        var stream = new SyncPointMemoryStream();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream));
            response.Headers.Add("custom", "value!");
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();
        call.Dispose();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);

        Assert.AreEqual("value!", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncUnaryCall_DisposeAfterHeadersAndBeforeMessage_ThrowOperationCanceledOnCancellation_ThrowsError()
    {
        // Arrange
        var stream = new SyncPointMemoryStream();

        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(stream));
            response.Headers.Add("custom", "value!");
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ThrowOperationCanceledOnCancellation = true);

        // Act
        var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest { Name = "World" });
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();
        call.Dispose();

        // Assert
        await ExceptionAssert.ThrowsAsync<TaskCanceledException>(() => call.ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);

        Assert.AreEqual("value!", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncUnaryCall_ErrorSendingRequest_ThrowsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new Exception("An error!"));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual("Error starting gRPC call. Exception: An error!", ex.Status.Detail);
        Assert.AreEqual("An error!", ex.Status.DebugException!.Message);
        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task AsyncUnaryCall_ErrorSendingRequest_ThrowsErrorWithInnerExceptionDetail()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new Exception("An error!", new Exception("Nested error!")));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual("Error starting gRPC call. Exception: An error! Exception: Nested error!", ex.Status.Detail);
        Assert.AreEqual("Nested error!", ex.Status.DebugException!.InnerException!.Message);
        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task AsyncClientStreamingCall_NotFoundStatus_ThrowsError()
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
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
        Assert.AreEqual("Bad gRPC response. HTTP status code: 404", ex.Status.Detail);
    }

    [Test]
    public async Task AsyncClientStreamingCall_InvalidContentType_ThrowsError()
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
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("Bad gRPC response. Invalid content-type value: text/plain", ex.Status.Detail);
    }
}
