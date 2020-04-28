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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class CorsTests : FunctionalTestBase
    {
        [Test]
        public async Task PreflightRequest_UnsupportedMethod_Return405()
        {
            // Arrange
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Options, "Greet.Greeter/SayHello");
            httpRequestMessage.Headers.Add("Origin", "http://localhost");
            httpRequestMessage.Headers.Add("Access-Control-Request-Method", "POST");
            httpRequestMessage.Version = new Version(2, 0);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequestMessage).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.AreEqual("POST", response.Content.Headers.Allow.Single());
        }

        [Test]
        public async Task PreflightRequest_SupportedMethod_Return405()
        {
            // Arrange
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Options, "Greet.SecondGreeter/SayHello");
            httpRequestMessage.Headers.Add("Origin", "http://localhost");
            httpRequestMessage.Headers.Add("Access-Control-Request-Method", "POST");
            httpRequestMessage.Version = new Version(2, 0);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequestMessage).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        }

        [Test]
        public async Task PreflightRequest_UnimplementedSupportedMethod_Return405()
        {
            // Arrange
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Options, "Greet.SecondGreeter/ThisIsNotImplemented");
            httpRequestMessage.Headers.Add("Origin", "http://localhost");
            httpRequestMessage.Headers.Add("Access-Control-Request-Method", "POST");
            httpRequestMessage.Version = new Version(2, 0);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequestMessage).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        }

        [Test]
        public async Task PreflightRequest_UnimplementedSupportedService_Return405()
        {
            // Arrange
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Options, "ThisIsNotImplemented/SayHello");
            httpRequestMessage.Headers.Add("Origin", "http://localhost");
            httpRequestMessage.Headers.Add("Access-Control-Request-Method", "POST");
            httpRequestMessage.Version = new Version(2, 0);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequestMessage).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
            Assert.AreEqual("POST", response.Headers.GetValues("Access-Control-Allow-Methods").Single());
        }
    }
}
