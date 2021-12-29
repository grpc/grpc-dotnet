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

using System.Net;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class DiagnosticsTests : FunctionalTestBase
    {
        [Test]
        public async Task HostActivityTags_ReturnedInTrailers_Success()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
            httpRequest.Content = new GrpcStreamContent(ms);
            httpRequest.Headers.Add("return-tags-trailers", "true");

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<HelloReply>().DefaultTimeout();
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();

            var trailers = response.TrailingHeaders;
            Assert.AreEqual("0", trailers.GetValues("grpc.status_code").Single());
            Assert.AreEqual("/Greet.Greeter/SayHello", trailers.GetValues("grpc.method").Single());
        }
    }
}
