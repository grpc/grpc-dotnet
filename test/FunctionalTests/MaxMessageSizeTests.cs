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
    public class MaxMessageSizeTests : FunctionalTestBase
    {
        [Test]
        public void ReceivedMessageExceedsSize_ThrowError()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World" + new string('!', 64 * 1024)
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var ex = Assert.ThrowsAsync<InvalidDataException>(() => Fixture.Client.PostAsync(
                "Greet.Greeter/SayHello",
                new StreamContent(ms)).DefaultTimeout());

            // Assert
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Message);
        }

        [Test]
        public void SentMessageExceedsSize_ThrowError()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => Fixture.Client.PostAsync(
                "Greet.Greeter/SayHelloSendLargeReply",
                new StreamContent(ms)).DefaultTimeout());

            // Assert
            Assert.AreEqual("Sending message exceeds the maximum configured message size.", ex.Message);
        }
    }
}
