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
using System.Text;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class ResponseHeadersAsyncTests
{
    [Test]
    public async Task AsyncUnaryCall_Success_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Server.Add(new ProductInfoHeaderValue("TestName", "1.0"));
            response.Headers.Add("custom", "ABC");
            response.Headers.Add("binary-bin", Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello world")));
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var responseHeaders1 = await call.ResponseHeadersAsync.DefaultTimeout();
        var responseHeaders2 = await call.ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreSame(responseHeaders1, responseHeaders2);

        Assert.AreEqual("TestName/1.0", responseHeaders1.GetValue("server"));

        Assert.AreEqual("ABC", responseHeaders1.GetValue("custom"));

        var header = responseHeaders1.Get("binary-bin")!;
        Assert.AreEqual(true, header.IsBinary);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes("Hello world"), header.ValueBytes);
    }

    [Test]
    public async Task AsyncUnaryCall_AuthInterceptorSuccess_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var credentialsSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var credentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            await credentialsSyncPoint.WaitToContinue();
            metadata.Add("Authorization", $"Bearer TEST");
        });

        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: options => options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials));

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var responseHeadersTask = call.ResponseHeadersAsync;

        await credentialsSyncPoint.WaitForSyncPoint().DefaultTimeout();
        credentialsSyncPoint.Continue();

        var responseHeaders = await responseHeadersTask.DefaultTimeout();

        // Assert
        Assert.AreEqual("ABC", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncUnaryCall_AuthInterceptorDispose_ResponseHeadersError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var reply = new HelloReply { Message = "Hello world" };
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var credentialsSyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var credentials = CallCredentials.FromInterceptor(async (context, metadata) =>
        {
            var tcs = new TaskCompletionSource<bool>();
            context.CancellationToken.Register(s => ((TaskCompletionSource<bool>)s!).SetResult(true), tcs);

            await Task.WhenAny(credentialsSyncPoint.WaitToContinue(), tcs.Task);
            metadata.Add("Authorization", $"Bearer TEST");
        });

        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: options => options.Credentials = ChannelCredentials.Create(new SslCredentials(), credentials));

        // Act
        var call = invoker.AsyncUnaryCall(new HelloRequest());
        var responseHeadersTask = call.ResponseHeadersAsync;

        await credentialsSyncPoint.WaitForSyncPoint().DefaultTimeout();

        call.Dispose();

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
    }

    [Test]
    public async Task AsyncClientStreamingCall_Success_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("ABC", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncDuplexStreamingCall_Success_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("ABC", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncServerStreamingCall_Success_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("ABC", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncServerStreamingCall_ErrorSendingRequest_ReturnsError()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            return Task.FromException<HttpResponseMessage>(new Exception("An error!"));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual("Error starting gRPC call. Exception: An error!", ex.Status.Detail);
        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task AsyncServerStreamingCall_DisposeBeforeHeadersReceived_ReturnsError()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpClient = ClientTestHelpers.CreateTestClient(async (request, ct) =>
        {
            await tcs.Task.DefaultTimeout();
            ct.ThrowIfCancellationRequested();
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        call.Dispose();
        tcs.TrySetResult(true);

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseHeadersAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task AsyncServerStreamingCall_DisposeBeforeHeadersReceived_ThrowOperationCanceledOnCancellation_ReturnsError()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var httpClient = ClientTestHelpers.CreateTestClient(async (request, ct) =>
        {
            await tcs.Task.DefaultTimeout();
            ct.ThrowIfCancellationRequested();
            var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            response.Headers.Add("custom", "ABC");
            return response;
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ThrowOperationCanceledOnCancellation = true);

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());
        call.Dispose();
        tcs.TrySetResult(true);

        // Assert
        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => call.ResponseHeadersAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task AsyncClientStreamingCall_NotFoundStatus_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.NotFound);
            response.Headers.Add("custom", "ABC");
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("ABC", responseHeaders.GetValue("custom"));
    }

    [Test]
    public async Task AsyncClientStreamingCall_InvalidContentType_ResponseHeadersPopulated()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            var response = ResponseUtils.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("custom", "ABC");
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return Task.FromResult(response);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncClientStreamingCall();
        var responseHeaders = await call.ResponseHeadersAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("ABC", responseHeaders.GetValue("custom"));
    }
}
