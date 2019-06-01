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
using System.Net;
using System.Threading.Tasks;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Tests.Shared;
using Nested;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class NestedTests : FunctionalTestBase
    {
        [Test]
        public async Task CallNestedService_SuccessResponse()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var response = await Fixture.Client.PostAsync(
                "Nested.NestedService/SayHello",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello Nested: World", responseMessage.Message);
            response.AssertTrailerStatus();
        }
    }
}
