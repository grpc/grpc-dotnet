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
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class CancellationTests
{
    [Test]
    public async Task AsyncClientStreamingCall_CancellationDuringSend_ResponseThrowsCancelledStatus()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var invoker = CreateTimedoutCallInvoker<HelloRequest, HelloReply>();

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        var responseTask = call.ResponseAsync;
        Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("Call canceled by the client.", call.GetStatus().Detail);
    }

    [Test]
    public async Task AsyncClientStreamingCall_CancellationDuringSend_ResponseHeadersThrowsCancelledStatus()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var invoker = CreateTimedoutCallInvoker<HelloRequest, HelloReply>();

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        var responseHeadersTask = call.ResponseHeadersAsync;
        Assert.IsFalse(responseHeadersTask.IsCompleted, "Headers not returned until client stream is complete.");

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => responseHeadersTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("Call canceled by the client.", call.GetStatus().Detail);
    }

    [Test]
    public async Task AsyncClientStreamingCall_CancellationDuringSend_ThrowOperationCanceledOnCancellation_ResponseThrowsCancelledStatus()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var invoker = CreateTimedoutCallInvoker<HelloRequest, HelloReply>(configure: o => o.ThrowOperationCanceledOnCancellation = true);

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        var responseTask = call.ResponseAsync;
        Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => responseTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("Call canceled by the client.", call.GetStatus().Detail);
        Assert.AreEqual(cts.Token, ((OperationCanceledException)call.GetStatus().DebugException!).CancellationToken);
        Assert.AreEqual(cts.Token, ex.CancellationToken);
    }

    [Test]
    public async Task AsyncClientStreamingCall_CancellationDuringSend_ThrowOperationCanceledOnCancellation_ResponseHeadersThrowsCancelledStatus()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var invoker = CreateTimedoutCallInvoker<HelloRequest, HelloReply>(configure: o => o.ThrowOperationCanceledOnCancellation = true);

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        var responseHeadersTask = call.ResponseHeadersAsync;
        Assert.IsFalse(responseHeadersTask.IsCompleted, "Headers not returned until client stream is complete.");

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => responseHeadersTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        Assert.AreEqual("Call canceled by the client.", call.GetStatus().Detail);
    }

    [Test]
    public void AsyncClientStreamingCall_CancellationDuringSend_TrailersThrowsInvalidOperation()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var invoker = CreateTimedoutCallInvoker<HelloRequest, HelloReply>();

        // Act
        var call = invoker.AsyncClientStreamingCall(new CallOptions(cancellationToken: cts.Token));

        // Assert
        cts.Cancel();

        var ex = Assert.Throws<InvalidOperationException>(() => call.GetTrailers())!;

        Assert.AreEqual("Can't get the call trailers because the call has not completed successfully.", ex.Message);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    private static HttpClientCallInvoker CreateTimedoutCallInvoker<TRequest, TResponse>(Action<GrpcChannelOptions>? configure = null)
        where TRequest : class
        where TResponse : class
    {
        var httpClient = ClientTestHelpers.CreateTestClient(async request =>
        {
            var content = (PushStreamContent<TRequest, TResponse>)request.Content!;
            await content.PushComplete.DefaultTimeout();

            return ResponseUtils.CreateResponse(HttpStatusCode.OK);
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: configure);
        return invoker;
    }
}
