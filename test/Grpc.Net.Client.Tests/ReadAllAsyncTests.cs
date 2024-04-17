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
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class ReadAllAsyncTests
{
    [Test]
    public async Task ReadAllAsync_MultipleMessages_MessagesReceived()
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

        var messages = new List<string>();
        await foreach (var item in call.ResponseStream.ReadAllAsync().DefaultTimeout())
        {
            messages.Add(item.Message);
        }

        // Assert
        Assert.AreEqual(2, messages.Count);
        Assert.AreEqual("Hello world 1", messages[0]);
        Assert.AreEqual("Hello world 2", messages[1]);
    }

    [Test]
    public void ReadAllAsync_StreamReaderNull_ThrowArgumentNullException()
    {
        // Arrange
        IAsyncStreamReader<int> reader = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => reader.ReadAllAsync());
    }

    [Test]
    public async Task ReadAllAsync_CancelCallViaWithCancellation_ForEachExitedOnCancellation()
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
        var cts = new CancellationTokenSource();

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var messages = new List<string>();
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () =>
            {
                await foreach (var item in call.ResponseStream.ReadAllAsync().DefaultTimeout().WithCancellation(cts.Token))
                {
                    messages.Add(item.Message);

                    cts.Cancel();
                }
            }).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual("Hello world 1", messages[0]);
    }

    [Test]
    public async Task ReadAllAsync_CancelCallViaArgument_ForEachExitedOnCancellation()
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
        var cts = new CancellationTokenSource();

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest());

        var messages = new List<string>();
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () =>
        {
            await foreach (var item in call.ResponseStream.ReadAllAsync(cts.Token).DefaultTimeout())
            {
                messages.Add(item.Message);

                cts.Cancel();
            }
        }).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(1, messages.Count);
        Assert.AreEqual("Hello world 1", messages[0]);
    }

    [Test]
    public async Task MoveNextAsync_MultipleMessages_MessagesReceived()
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

        var enumerator = call.ResponseStream.ReadAllAsync().GetAsyncEnumerator();

        // Assert
        Assert.IsNull(enumerator.Current);

        Assert.IsTrue(await enumerator.MoveNextAsync().AsTask().DefaultTimeout());
        Assert.IsNotNull(enumerator.Current);
        Assert.AreEqual("Hello world 1", enumerator.Current.Message);

        Assert.IsTrue(await enumerator.MoveNextAsync().AsTask().DefaultTimeout());
        Assert.IsNotNull(enumerator.Current);
        Assert.AreEqual("Hello world 2", enumerator.Current.Message);

        Assert.IsFalse(await enumerator.MoveNextAsync().AsTask().DefaultTimeout());
    }

    [Test]
    public async Task MoveNextAsync_CancelCall_EnumeratorThrows()
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
        var cts = new CancellationTokenSource();

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest(), new CallOptions(cancellationToken: cts.Token));

        var enumerator = call.ResponseStream.ReadAllAsync().GetAsyncEnumerator();

        // Assert
        Assert.IsNull(enumerator.Current);

        Assert.IsTrue(await enumerator.MoveNextAsync().AsTask().DefaultTimeout());
        Assert.IsNotNull(enumerator.Current);
        Assert.AreEqual("Hello world 1", enumerator.Current.Message);

        cts.Cancel();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => enumerator.MoveNextAsync().AsTask()).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }

    [Test]
    public async Task MoveNextAsync_CancelCall_ThrowOperationCanceledOnCancellation_EnumeratorThrows()
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
        var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.ThrowOperationCanceledOnCancellation = true);
        var cts = new CancellationTokenSource();

        // Act
        var call = invoker.AsyncServerStreamingCall(new HelloRequest(), new CallOptions(cancellationToken: cts.Token));

        var enumerator = call.ResponseStream.ReadAllAsync().GetAsyncEnumerator();

        // Assert
        Assert.IsNull(enumerator.Current);

        Assert.IsTrue(await enumerator.MoveNextAsync().AsTask().DefaultTimeout());
        Assert.IsNotNull(enumerator.Current);
        Assert.AreEqual("Hello world 1", enumerator.Current.Message);

        cts.Cancel();

        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => enumerator.MoveNextAsync().AsTask()).DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
    }
}
