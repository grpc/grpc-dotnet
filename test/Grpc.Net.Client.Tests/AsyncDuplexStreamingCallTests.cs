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
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Http;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class AsyncDuplexStreamingCallTests
{
    [Test]
    public async Task AsyncDuplexStreamingCall_NoContent_NoMessagesReturned()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>())));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);
        Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        Assert.IsNull(responseStream.Current);
    }

    [Test]
    public async Task AsyncServerStreamingCall_MessagesReturnedTogether_MessagesReceived()
    {
        // Arrange
        var httpClient = ClientTestHelpers.CreateTestClient(request =>
        {
            HelloReply reply = new HelloReply
            {
                Message = "Hello world"
            };

            return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, new ByteArrayContent(Array.Empty<byte>())));
        });
        var invoker = HttpClientCallInvokerFactory.Create(httpClient);

        // Act
        var call = invoker.AsyncDuplexStreamingCall();

        var responseStream = call.ResponseStream;

        // Assert
        Assert.IsNull(responseStream.Current);
        Assert.IsFalse(await responseStream.MoveNext(CancellationToken.None).DefaultTimeout());
        Assert.IsNull(responseStream.Current);
    }

    [Test]
    public async Task AsyncDuplexStreamingCall_MessagesStreamed_MessagesReceived()
    {
        // Arrange
        var streamContent = new SyncPointMemoryStream();
        var requestContentTcs = new TaskCompletionSource<Task<Stream>>(TaskCreationOptions.RunContinuationsAsynchronously);

        PushStreamContent<HelloRequest, HelloReply>? content = null;

        var handler = TestHttpMessageHandler.Create(async request =>
        {
            content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
            var streamTask = content.ReadAsStreamAsync();
            requestContentTcs.SetResult(streamTask);

            // Wait for RequestStream.CompleteAsync()
            await streamTask;

            return ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent));
        });
        var invoker = HttpClientCallInvokerFactory.Create(handler, "https://localhost");

        // Act
        var call = invoker.AsyncDuplexStreamingCall();

        var requestStream = call.RequestStream;
        var responseStream = call.ResponseStream;

        // Assert
        await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
        await call.RequestStream.WriteAsync(new HelloRequest { Name = "2" }).DefaultTimeout();

        await call.RequestStream.CompleteAsync().DefaultTimeout();

        var deserializationContext = new DefaultDeserializationContext();

        Assert.IsNotNull(content);
        var requestContent = await await requestContentTcs.Task.DefaultTimeout();
        var requestMessage = await StreamSerializationHelper.ReadMessageAsync(
            requestContent,
            ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
            GrpcProtocolConstants.IdentityGrpcEncoding,
            maximumMessageSize: null,
            GrpcProtocolConstants.DefaultCompressionProviders,
            singleMessage: false,
            CancellationToken.None).DefaultTimeout();
        Assert.AreEqual("1", requestMessage!.Name);
        requestMessage = await StreamSerializationHelper.ReadMessageAsync(
            requestContent,
            ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
            GrpcProtocolConstants.IdentityGrpcEncoding,
            maximumMessageSize: null,
            GrpcProtocolConstants.DefaultCompressionProviders,
            singleMessage: false,
            CancellationToken.None).DefaultTimeout();
        Assert.AreEqual("2", requestMessage!.Name);

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

        await streamContent.AddDataAndWait(await ClientTestHelpers.GetResponseDataAsync(new HelloReply
        {
            Message = "Hello world 2"
        }).DefaultTimeout()).DefaultTimeout();

        Assert.IsTrue(await moveNextTask2.DefaultTimeout());
        Assert.IsNotNull(responseStream.Current);
        Assert.AreEqual("Hello world 2", responseStream.Current.Message);

        var moveNextTask3 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsFalse(moveNextTask3.IsCompleted);

        await streamContent.EndStreamAndWait().DefaultTimeout();

        Assert.IsFalse(await moveNextTask3.DefaultTimeout());

        var moveNextTask4 = responseStream.MoveNext(CancellationToken.None);
        Assert.IsTrue(moveNextTask4.IsCompleted);
        Assert.IsFalse(await moveNextTask3.DefaultTimeout());
    }

    [Test]
    public async Task AsyncDuplexStreamingCall_CancellationDisposeRace_Success()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        var loggerFactory = services.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger(GetType());

        for (var i = 0; i < 20; i++)
        {
            // Let's mimic a real call first to get GrpcCall.RunCall where we need to for reproducing the deadlock.
            var streamContent = new SyncPointMemoryStream();
            var requestContentTcs = new TaskCompletionSource<Task<Stream>>(TaskCreationOptions.RunContinuationsAsynchronously);

            PushStreamContent<HelloRequest, HelloReply>? content = null;

            var handler = TestHttpMessageHandler.Create(async request =>
            {
                content = (PushStreamContent<HelloRequest, HelloReply>)request.Content!;
                var streamTask = content.ReadAsStreamAsync();
                requestContentTcs.SetResult(streamTask);
                // Wait for RequestStream.CompleteAsync()
                await streamTask;
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, new StreamContent(streamContent));
            });
            var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler,
                LoggerFactory = loggerFactory
            });
            var invoker = channel.CreateCallInvoker();

            var cts = new CancellationTokenSource();

            var call = invoker.AsyncDuplexStreamingCall(new CallOptions(cancellationToken: cts.Token));
            await call.RequestStream.WriteAsync(new HelloRequest { Name = "1" }).DefaultTimeout();
            await call.RequestStream.CompleteAsync().DefaultTimeout();

            // Let's read a response
            var deserializationContext = new DefaultDeserializationContext();
            var requestContent = await await requestContentTcs.Task.DefaultTimeout();
            var requestMessage = await StreamSerializationHelper.ReadMessageAsync(
                requestContent,
                ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                GrpcProtocolConstants.IdentityGrpcEncoding,
                maximumMessageSize: null,
                GrpcProtocolConstants.DefaultCompressionProviders,
                singleMessage: false,
                CancellationToken.None).DefaultTimeout();
            Assert.AreEqual("1", requestMessage!.Name);

            var actTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var cancellationTask = Task.Run(async () =>
            {
                await actTcs.Task;
                cts.Cancel();
            });
            var disposingTask = Task.Run(async () =>
            {
                await actTcs.Task;
                channel.Dispose();
            });

            // Small pause to make sure we're waiting at the TCS everywhere.
            await Task.Delay(50);

            // Act
            actTcs.SetResult(null);

            // Assert
            // Cancellation and disposing should both complete quickly. If there is a deadlock then the await will timeout.
            await Task.WhenAll(cancellationTask, disposingTask).DefaultTimeout();
        }
    }
}
