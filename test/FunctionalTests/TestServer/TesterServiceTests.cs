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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;
using Test;

namespace Grpc.AspNetCore.FunctionalTests.TestServer
{
    [TestFixture]
    public class TesterServiceTests : FunctionalTestBase
    {
        [Test]
        public async Task UnaryTest_Success()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            // Act
            var response = await client.SayHelloUnaryAsync(new HelloRequest { Name = "Joe" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Hello Joe", response.Message);
        }

        [Test]
        public async Task UnaryTest_Error()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.SayHelloUnaryErrorAsync(new HelloRequest { Name = "Joe" }).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);
        }

        [Test]
        public async Task ClientStreamingTest_Success()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            var names = new[] { "James", "Jo", "Lee" };
            HelloReply response;

            // Act
            using (var call = client.SayHelloClientStreaming())
            {
                foreach (var name in names)
                {
                    await call.RequestStream.WriteAsync(new HelloRequest { Name = name }).DefaultTimeout();
                    await Task.Delay(50);
                }
                await call.RequestStream.CompleteAsync().DefaultTimeout();

                response = await call.ResponseAsync.DefaultTimeout();
            }

            // Assert
            Assert.AreEqual("Hello James, Jo, Lee", response.Message);
        }

        [Test]
        public async Task ClientStreamingTest_Error()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            // Act
            using var call = client.SayHelloClientStreamingError();

            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                var names = new[] { "James", "Jo", "Lee" };

                {
                    foreach (var name in names)
                    {
                        await call.RequestStream.WriteAsync(new HelloRequest { Name = name }).DefaultTimeout();
                        await Task.Delay(50);
                    }
                }
            }).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.NotFound, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task ServerStreamingTest_Success()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            var cts = new CancellationTokenSource();
            var hasMessages = false;
            var callCancelled = false;

            // Act
            using (var call = client.SayHelloServerStreaming(new HelloRequest { Name = "Joe" }, cancellationToken: cts.Token))
            {
                try
                {
                    await foreach (var message in call.ResponseStream.ReadAllAsync().DefaultTimeout())
                    {
                        hasMessages = true;
                        cts.Cancel();
                    }
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                {
                    callCancelled = true;
                }
            }

            // Assert
            Assert.IsTrue(hasMessages);
            Assert.IsTrue(callCancelled);
        }

        [Test]
        public async Task ServerStreamingTest_Throw()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            // Act
            using var call = client.SayHelloServerStreamingError(new HelloRequest { Name = "Joe" });

            // Assert
            Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();

            Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);
        }

        [Test]
        public async Task BidirectionStreamingTest_Success()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            var names = new[] { "James", "Jo", "Lee" };
            var messages = new List<string>();

            // Act
            using (var call = client.SayHelloBidirectionalStreaming())
            {
                foreach (var name in names)
                {
                    await call.RequestStream.WriteAsync(new HelloRequest { Name = name }).DefaultTimeout();

                    Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
                    messages.Add(call.ResponseStream.Current.Message);
                }

                await call.RequestStream.CompleteAsync().DefaultTimeout();

                Assert.IsFalse(await call.ResponseStream.MoveNext().DefaultTimeout());
            }

            // Assert
            Assert.AreEqual(3, messages.Count);
            Assert.AreEqual("Hello James", messages[0]);
        }

        [Test]
        public async Task BidirectionStreamingTest_Error()
        {
            // Arrange
            var client = new Tester.TesterClient(Channel);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () =>
            {
                var names = new[] { "James", "Jo", "Lee" };

                using (var call = client.SayHelloBidirectionalStreamingError())
                {
                    foreach (var name in names)
                    {
                        await call.RequestStream.WriteAsync(new HelloRequest { Name = name }).DefaultTimeout();

                        Assert.IsTrue(await call.ResponseStream.MoveNext().DefaultTimeout());
                    }

                    await call.RequestStream.CompleteAsync().DefaultTimeout();

                    Assert.IsFalse(await call.ResponseStream.MoveNext().DefaultTimeout());
                }
            }).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);
        }
    }
}
