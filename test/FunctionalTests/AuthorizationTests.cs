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
using System.Net.Http;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class AuthorizationTests : FunctionalTestBase
    {
        [Test]
        public async Task CallAuthorizedServiceWithoutToken_Unauthorized()
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
                "Authorize.AuthorizedGreeter/SayHello",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Test]
        public async Task CallAuthorizedServiceWithToken_Success()
        {
            // Arrange
            var tokenResponse = await Fixture.Client.GetAsync("generateJwtToken").DefaultTimeout();
            var token = await tokenResponse.Content.ReadAsStringAsync();

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Authorize.AuthorizedGreeter/SayHello");
            httpRequest.Headers.Add("Authorization", $"Bearer {token}");
            httpRequest.Content = new GrpcStreamContent(ms);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest);

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<HelloReply>();
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task CallAuthorizedServiceWithInvalidToken_ReturnUnauthorized()
        {
            // Arrange

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Authorize.AuthorizedGreeter/SayHello");
            httpRequest.Headers.Add("Authorization", $"Bearer SomeInvalidTokenHere");
            httpRequest.Content = new GrpcStreamContent(ms);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest);

            // Assert
            Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
