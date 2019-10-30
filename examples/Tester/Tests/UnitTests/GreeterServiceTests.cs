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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Server;
using Test;
using Tests.UnitTests.Helpers;

namespace Tests.UnitTests
{
    [TestFixture]
    public class GreeterServiceTests
    {
        [Test]
        public async Task SayHelloUnaryTest()
        {
            // Arrange
            var service = new TesterService(NullLoggerFactory.Instance);

            // Act
            var response = await service.SayHelloUnary(new HelloRequest { Name = "Joe" }, TestServerCallContext.Create());

            // Assert
            Assert.AreEqual("Hello Joe", response.Message);
        }

        [Test]
        public async Task SayHelloServerStreamingTest()
        {
            // Arrange
            var service = new TesterService(NullLoggerFactory.Instance);

            var cts = new CancellationTokenSource();
            var callContext = TestServerCallContext.Create(cancellationToken: cts.Token);
            var responseStream = new TestServerStreamWriter<HelloReply>(callContext);

            // Act
            var call = service.SayHelloServerStreaming(new HelloRequest { Name = "Joe" }, responseStream, callContext);

            // Assert
            Assert.IsFalse(call.IsCompletedSuccessfully, "Method should run until cancelled.");

            cts.Cancel();

            await call;
            responseStream.Complete();

            var allMessages = new List<HelloReply>();
            await foreach (var message in responseStream.ReadAllAsync())
            {
                allMessages.Add(message);
            }

            Assert.GreaterOrEqual(allMessages.Count, 1);

            Assert.AreEqual("How are you Joe? 1", allMessages[0].Message);
        }

        [Test]
        public async Task SayHelloClientStreamingTest()
        {
            // Arrange
            var service = new TesterService(NullLoggerFactory.Instance);

            var callContext = TestServerCallContext.Create();
            var requestStream = new TestAsyncStreamReader<HelloRequest>(callContext);

            // Act
            var call = service.SayHelloClientStreaming(requestStream, callContext);

            requestStream.AddMessage(new HelloRequest { Name = "James" });
            requestStream.AddMessage(new HelloRequest { Name = "Jo" });
            requestStream.AddMessage(new HelloRequest { Name = "Lee" });
            requestStream.Complete();

            // Assert
            var response = await call;
            Assert.AreEqual("Hello James, Jo, Lee", response.Message);
        }

        [Test]
        public async Task SayHelloBidirectionStreamingTest()
        {
            // Arrange
            var service = new TesterService(NullLoggerFactory.Instance);

            var callContext = TestServerCallContext.Create();
            var requestStream = new TestAsyncStreamReader<HelloRequest>(callContext);
            var responseStream = new TestServerStreamWriter<HelloReply>(callContext);

            // Act
            var call = service.SayHelloBidirectionalStreaming(requestStream, responseStream, callContext);

            // Assert
            requestStream.AddMessage(new HelloRequest { Name = "James" });
            Assert.AreEqual("Hello James", (await responseStream.ReadNextAsync())!.Message);

            requestStream.AddMessage(new HelloRequest { Name = "Jo" });
            Assert.AreEqual("Hello Jo", (await responseStream.ReadNextAsync())!.Message);

            requestStream.AddMessage(new HelloRequest { Name = "Lee" });
            Assert.AreEqual("Hello Lee", (await responseStream.ReadNextAsync())!.Message);

            requestStream.Complete();

            await call;
            responseStream.Complete();

            Assert.IsNull(await responseStream.ReadNextAsync());
        }
    }
}
