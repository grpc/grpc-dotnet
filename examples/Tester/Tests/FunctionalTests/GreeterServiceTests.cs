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
using Grpc.Core;
using NUnit.Framework;
using Test;

namespace Tests.FunctionalTests
{
    [TestFixture]
    public class GreeterServiceTests : FunctionalTestBase
    {
        [Test]
        public async Task SayHelloUnaryTest()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            // Act
            var response = await client.SayHelloUnaryAsync(new HelloRequest { Name = "Joe" });

            // Assert
            Assert.AreEqual("Hello Joe", response.Message);
        }

        [Test]
        public async Task SayHelloClientStreamingTest()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            var names = new[] { "James", "Jo", "Lee" };
            HelloReply response;

            // Act
            using var call = client.SayHelloClientStreaming();
            foreach (var name in names)
            {
                await call.RequestStream.WriteAsync(new HelloRequest { Name = name });
            }
            await call.RequestStream.CompleteAsync();

            response = await call;

            // Assert
            Assert.AreEqual("Hello James, Jo, Lee", response.Message);
        }

        [Test]
        public async Task SayHelloServerStreamingTest()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            var cts = new CancellationTokenSource();
            var hasMessages = false;
            var callCancelled = false;

            // Act
            using var call = client.SayHelloServerStreaming(new HelloRequest { Name = "Joe" }, cancellationToken: cts.Token);
            try
            {
                await foreach (var message in call.ResponseStream.ReadAllAsync())
                {
                    hasMessages = true;
                    cts.Cancel();
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                callCancelled = true;
            }

            // Assert
            Assert.IsTrue(hasMessages);
            Assert.IsTrue(callCancelled);
        }

        [Test]
        public async Task SayHelloBidirectionStreamingTest()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            var names = new[] { "James", "Jo", "Lee" };
            var messages = new List<string>();

            // Act
            using var call = client.SayHelloBidirectionalStreaming();
            foreach (var name in names)
            {
                await call.RequestStream.WriteAsync(new HelloRequest { Name = name });

                Assert.IsTrue(await call.ResponseStream.MoveNext());
                messages.Add(call.ResponseStream.Current.Message);
            }

            await call.RequestStream.CompleteAsync();

            // Assert
            Assert.AreEqual(3, messages.Count);
            Assert.AreEqual("Hello James", messages[0]);
        }
    }
}
