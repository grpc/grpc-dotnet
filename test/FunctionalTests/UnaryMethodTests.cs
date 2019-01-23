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
    public class UnaryMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task SayHello_ValidRequest_SuccessResponse()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var stream = new MemoryStream();
            await MessageHelpers.WriteMessageAsync(stream, requestMessage).DefaultTimeout();

            // Act
            var response = await Fixture.Client.PostAsync(
                "Greet.Greeter/SayHello",
                new StreamContent(stream)).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);
        }
    }
}
