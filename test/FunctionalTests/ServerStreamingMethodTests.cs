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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
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
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();

            for (var i = 0; i < 3; i++)
            {
                var greetingTask = MessageHelpers.AssertReadMessageStreamAsync<HelloReply>(responseStream);

                // The first response comes with the headers
                // Additional responses are streamed
                Assert.AreEqual(i == 0, greetingTask.IsCompleted);

                var greeting = await greetingTask.DefaultTimeout();

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbyeTask = MessageHelpers.AssertReadMessageStreamAsync<HelloReply>(responseStream);
            Assert.False(goodbyeTask.IsCompleted);
            Assert.AreEqual("Goodbye World!", (await goodbyeTask.DefaultTimeout()).Message);

            var finishedTask = MessageHelpers.AssertReadMessageStreamAsync<HelloReply>(responseStream);
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
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();

            for (var i = 0; i < 3; i++)
            {
                var greetingTask = MessageHelpers.AssertReadMessageStreamAsync<HelloReply>(responseStream);

                // The headers are already sent
                // All responses are streamed
                Assert.False(greetingTask.IsCompleted);

                var greeting = await greetingTask.DefaultTimeout();

                Assert.AreEqual($"How are you World? {i}", greeting.Message);
            }

            var goodbyeTask = MessageHelpers.AssertReadMessageStreamAsync<HelloReply>(responseStream);
            Assert.False(goodbyeTask.IsCompleted);
            Assert.AreEqual("Goodbye World!", (await goodbyeTask.DefaultTimeout()).Message);

            var finishedTask = MessageHelpers.AssertReadMessageStreamAsync<HelloReply>(responseStream);
            Assert.IsNull(await finishedTask.DefaultTimeout());
        }
    }
}
