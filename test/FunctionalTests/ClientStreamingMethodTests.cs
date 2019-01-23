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
using Count;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class ClientStreamingMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task AccumulateCount_MultipleMessagesThenClose_SuccessResponse()
        {
            // Arrange
            var ms = new MemoryStream();
            await MessageHelpers.WriteMessageAsync(ms, new CounterRequest
            {
                Count = 1
            }).DefaultTimeout();

            var requestStream = new AwaitableMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Count.Counter/AccumulateCount");
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            Fixture.Signal.Reset();
            requestStream.SendData(ms.ToArray());
            await Fixture.Signal.Task.DefaultTimeout();

            Fixture.Signal.Reset();
            requestStream.SendData(ms.ToArray());
            await Fixture.Signal.Task.DefaultTimeout();

            Fixture.Signal.Reset();
            requestStream.SendData(Array.Empty<byte>());
            await Fixture.Signal.Task.DefaultTimeout();

            var response = await responseTask.DefaultTimeout();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();

            var replyTask = MessageHelpers.AssertReadMessageAsync<CounterReply>(responseStream);
            Assert.IsTrue(replyTask.IsCompleted);
            var reply = await replyTask.DefaultTimeout();
            Assert.AreEqual(2, reply.Count);
        }
    }
}
