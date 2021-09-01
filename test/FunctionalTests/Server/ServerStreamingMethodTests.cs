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

using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using FunctionalTestsWebsite.Services;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class ServerStreamingMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task NoBuffering_SuccessResponsesStreamed()
        {
            using var httpEventSource = new HttpEventSourceListener(LoggerFactory);

            var methodWrapper = new MethodWrapper
            {
                Logger = Logger,
                SyncPoint = new SyncPoint(runContinuationsAsynchronously: true)
            };

            async Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                // Explicitly send the response headers before any streamed content
                Metadata responseHeaders = new Metadata();
                responseHeaders.Add("test-response-header", "value");
                await context.WriteResponseHeadersAsync(responseHeaders);

                await methodWrapper.SayHellosAsync(request, responseStream);
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(SayHellos);

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            for (var i = 0; i < 3; i++)
            {
                var greetingTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);

                Assert.IsFalse(greetingTask.IsCompleted);

                await methodWrapper.SyncPoint.WaitForSyncPoint().DefaultTimeout();

                var currentSyncPoint = methodWrapper.SyncPoint;
                methodWrapper.SyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
                currentSyncPoint.Continue();

                var greeting = (await greetingTask.DefaultTimeout())!;

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbyeTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.False(goodbyeTask.IsCompleted);

            await methodWrapper.SyncPoint.WaitForSyncPoint().DefaultTimeout();
            methodWrapper.SyncPoint.Continue();

            Assert.AreEqual("Goodbye World!", (await goodbyeTask.DefaultTimeout())!.Message);

            var finishedTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.IsNull(await finishedTask.DefaultTimeout());
        }

        [Test]
        public async Task WriteResponseHeadersAsyncCore_FlushesHeadersToClient()
        {
            var methodWrapper = new MethodWrapper
            {
                Logger = Logger,
                SyncPoint = new SyncPoint(runContinuationsAsynchronously: true)
            };

            async Task SayHellosSendHeadersFirst(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await context.WriteResponseHeadersAsync(null);

                await methodWrapper.SayHellosAsync(request, responseStream);
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(SayHellosSendHeadersFirst);

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            Logger.LogInformation("Headers received. Starting to read stream");

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            for (var i = 0; i < 3; i++)
            {
                Logger.LogInformation($"Reading message {i}.");

                var greetingTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);

                // The headers are already sent
                // All responses are streamed
                Logger.LogInformation($"Message task completed: {greetingTask.IsCompleted}");
                Assert.IsFalse(greetingTask.IsCompleted);

                await methodWrapper.SyncPoint.WaitForSyncPoint().DefaultTimeout();

                var currentSyncPoint = methodWrapper.SyncPoint;
                methodWrapper.SyncPoint = new SyncPoint(runContinuationsAsynchronously: true);
                currentSyncPoint.Continue();

                var greeting = (await greetingTask.DefaultTimeout())!;

                Logger.LogInformation($"Received message {i}: {greeting.Message}");

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbyeTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.False(goodbyeTask.IsCompleted);

            await methodWrapper.SyncPoint.WaitForSyncPoint().DefaultTimeout();
            methodWrapper.SyncPoint.Continue();

            Assert.AreEqual("Goodbye World!", (await goodbyeTask.DefaultTimeout())!.Message);

            var finishedTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.IsNull(await finishedTask.DefaultTimeout());
        }

        [Test]
        public async Task Buffering_SuccessResponsesStreamed()
        {
            var methodWrapper = new MethodWrapper
            {
                Logger = Logger,
                SyncPoint = new SyncPoint(runContinuationsAsynchronously: true)
            };

            Task SayHellosBufferHint(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                context.WriteOptions = new WriteOptions(WriteFlags.BufferHint);

                return methodWrapper.SayHellosAsync(request, responseStream);
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(SayHellosBufferHint);

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for first message from client");

            await methodWrapper.SyncPoint.WaitForSyncPoint().DefaultTimeout();
            methodWrapper.SyncPoint.Continue();

            var response = await responseTask.DefaultTimeout();
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            for (var i = 0; i < 3; i++)
            {
                var greeting = (await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout())!;

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbye = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout();
            Assert.AreEqual("Goodbye World!", goodbye!.Message);

            Assert.IsNull(await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout());

            response.AssertTrailerStatus();
        }

        public class MethodWrapper
        {
            public SyncPoint SyncPoint { get; set; } = default!;
            public ILogger Logger { get; set; } = default!;

            public async Task SayHellosAsync(HelloRequest request, IServerStreamWriter<HelloReply> responseStream)
            {
                for (var i = 0; i < 3; i++)
                {
                    await SyncPoint.WaitToContinue();

                    var message = $"How are you {request.Name}? {i}";

                    Logger.LogInformation("Sending message");
                    await responseStream.WriteAsync(new HelloReply { Message = message });
                }

                await SyncPoint.WaitToContinue();

                Logger.LogInformation("Sending message");
                await responseStream.WriteAsync(new HelloReply { Message = $"Goodbye {request.Name}!" });
            }
        }
    }
}
