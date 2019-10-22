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

using System.Threading;
using System.Threading.Tasks;
using Test;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Server;

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
            var responseTcs = new TaskCompletionSource<HelloReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var responseStream = new TestServerStreamWriter<HelloReply>(callContext, message => responseTcs.TrySetResult(message));

            // Act
            var call = service.SayHelloServerStreaming(new HelloRequest { Name = "Joe" }, responseStream, callContext);

            // Assert
            Assert.IsFalse(call.IsCompletedSuccessfully, "Method should run until cancelled.");

            var firstResponse = await responseTcs.Task;
            Assert.AreEqual("How are you Joe? 1", firstResponse.Message);

            cts.Cancel();
            await call;
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
    }
}
