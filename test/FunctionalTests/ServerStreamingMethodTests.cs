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
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class ServerStreamingMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task NoBuffering_SuccessResponsesStreamed()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHellos");
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

                var greeting = await greetingTask.DefaultTimeout();

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbyeTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.False(goodbyeTask.IsCompleted);
            Assert.AreEqual("Goodbye World!", (await goodbyeTask.DefaultTimeout()).Message);

            var finishedTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.IsNull(await finishedTask.DefaultTimeout());
        }

        [Test]
        public async Task WriteResponseHeadersAsyncCore_FlushesHeadersToClient()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHellosSendHeadersFirst");
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

                // The headers are already sent
                // All responses are streamed
                Assert.False(greetingTask.IsCompleted);

                var greeting = await greetingTask.DefaultTimeout();

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbyeTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.False(goodbyeTask.IsCompleted);
            Assert.AreEqual("Goodbye World!", (await goodbyeTask.DefaultTimeout()).Message);

            var finishedTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
            Assert.IsNull(await finishedTask.DefaultTimeout());
        }

        [Test]
        public async Task Buffering_SuccessResponsesStreamed()
        {
            static Task SayHellosBufferHint(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                context.WriteOptions = new WriteOptions(WriteFlags.BufferHint);

                return GreeterService.SayHellosCore(request, responseStream);
            }

            // Arrange
            var url = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(SayHellosBufferHint);

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for first message from client");

            var response = await responseTask;
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            for (var i = 0; i < 3; i++)
            {
                var greeting = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout();

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbye = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout();
            Assert.AreEqual("Goodbye World!", goodbye.Message);

            Assert.IsNull(await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout());

            response.AssertTrailerStatus();
        }
    }
}
