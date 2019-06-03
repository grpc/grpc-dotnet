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
using System.IO;
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using Chat;
using FunctionalTestsWebsite.Services;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class DuplexStreamingMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task MultipleMessagesFromOneClient_SuccessResponses()
        {
            // Arrange
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new ChatMessage
            {
                Name = "John",
                Message = "Hello Jill"
            });

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Chat.Chatter/Chat");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for first message from client");
            requestStream.AddData(ms.ToArray());

            var response = await responseTask.DefaultTimeout();
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            var message1Task = MessageHelpers.AssertReadStreamMessageAsync<ChatMessage>(pipeReader);
            var message1 = await message1Task.DefaultTimeout();
            Assert.AreEqual("John", message1.Name);
            Assert.AreEqual("Hello Jill", message1.Message);

            var message2Task = MessageHelpers.AssertReadStreamMessageAsync<ChatMessage>(pipeReader);
            Assert.IsFalse(message2Task.IsCompleted, "Server is waiting for messages from client");

            ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new ChatMessage
            {
                Name = "Jill",
                Message = "Hello John"
            });

            requestStream.AddData(ms.ToArray());
            var message2 = await message2Task.DefaultTimeout();
            Assert.AreEqual("Jill", message2.Name);
            Assert.AreEqual("Hello John", message2.Message);

            var finishedTask = MessageHelpers.AssertReadStreamMessageAsync<ChatMessage>(pipeReader);
            Assert.IsFalse(finishedTask.IsCompleted, "Server is waiting for client to end streaming");

            requestStream.AddData(Array.Empty<byte>());
            await finishedTask.DefaultTimeout();

            response.AssertTrailerStatus();
        }

        [Test]
        public async Task BufferHint_SuccessResponses()
        {
            static Task ChatBufferHint(IAsyncStreamReader<ChatMessage> requestStream, IServerStreamWriter<ChatMessage> responseStream, ServerCallContext context)
            {
                context.WriteOptions = new WriteOptions(WriteFlags.BufferHint);

                return ChatterService.ChatCore(requestStream, responseStream);
            }

            // Arrange
            var url = Fixture.DynamicGrpc.AddDuplexStreamingMethod<ChatMessage, ChatMessage>(ChatBufferHint);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new ChatMessage
            {
                Name = "John",
                Message = "Hello Jill"
            });

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for first message from client");

            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
            Assert.IsFalse(responseTask.IsCompleted, "Server is buffering response 1");

            ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new ChatMessage
            {
                Name = "Jill",
                Message = "Hello John"
            });

            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
            Assert.IsFalse(responseTask.IsCompleted, "Server is buffering response 2");

            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            var response = await responseTask.DefaultTimeout();
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            var message1 = await MessageHelpers.AssertReadStreamMessageAsync<ChatMessage>(pipeReader).DefaultTimeout();
            Assert.AreEqual("John", message1.Name);
            Assert.AreEqual("Hello Jill", message1.Message);

            var message2 = await MessageHelpers.AssertReadStreamMessageAsync<ChatMessage>(pipeReader).DefaultTimeout();
            Assert.AreEqual("Jill", message2.Name);
            Assert.AreEqual("Hello John", message2.Message);

            Assert.IsNull(await MessageHelpers.AssertReadStreamMessageAsync<ChatMessage>(pipeReader).DefaultTimeout());

            response.AssertTrailerStatus();
        }
    }
}
