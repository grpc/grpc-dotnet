#region Copyright notice and license

// Copyright 2015 gRPC authors.
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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Chat;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class DuplexStreamingMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task Chat_MultipleMessagesFromOneClient_SuccessResponses()
        {
            // Arrange
            var ms = new MemoryStream();
            await MessageHelpers.WriteMessageAsync(ms, new ChatMessage
            {
                Name = "John",
                Message = "Hello Jill"
            }).DefaultTimeout();

            var requestStream = new AwaitableMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Chat.Chatter/Chat");
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for first message from client");
            requestStream.SendData(ms.ToArray());

            var response = await responseTask.DefaultTimeout();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();

            var message1Task = MessageHelpers.AssertReadMessageAsync<ChatMessage>(responseStream);
            Assert.IsTrue(message1Task.IsCompleted);
            var message1 = await message1Task.DefaultTimeout();
            Assert.AreEqual("John", message1.Name);
            Assert.AreEqual("Hello Jill", message1.Message);

            var message2Task = MessageHelpers.AssertReadMessageAsync<ChatMessage>(responseStream);
            Assert.IsFalse(message2Task.IsCompleted, "Server is waiting for messages from client");

            ms = new MemoryStream();
            await MessageHelpers.WriteMessageAsync(ms, new ChatMessage
            {
                Name = "Jill",
                Message = "Hello John"
            }).DefaultTimeout();

            requestStream.SendData(ms.ToArray());
            var message2 = await message2Task.DefaultTimeout();
            Assert.AreEqual("Jill", message2.Name);
            Assert.AreEqual("Hello John", message2.Message);

            var finishedTask = MessageHelpers.AssertReadMessageAsync<ChatMessage>(responseStream);
            Assert.IsFalse(finishedTask.IsCompleted, "Server is waiting for client to end streaming");

            requestStream.SendData(Array.Empty<byte>());
            await finishedTask.DefaultTimeout();
        }
    }
}
