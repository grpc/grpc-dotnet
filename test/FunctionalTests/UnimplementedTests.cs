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
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests;
using Grpc.Core;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class UnimplementedTests : FunctionalTestBase
    {
        [Test]
        public async Task HttpContextAccessor_ReturnContextInTrailer()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/MethodDoesNotExist");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            Assert.AreEqual(StatusCode.Unimplemented.ToTrailerString(), Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.StatusTrailer].ToString());
            Assert.AreEqual("Method is unimplemented.", Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.MessageTrailer].ToString());
        }

        [Test]
        public async Task HttpContextExtensionMethod_ReturnContextInTrailer()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.ServiceDoesNotExist/Method");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            Assert.AreEqual(StatusCode.Unimplemented.ToTrailerString(), Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.StatusTrailer].ToString());
            Assert.AreEqual("Service is unimplemented.", Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.MessageTrailer].ToString());
        }

        [TestCase("application/grpc", HttpStatusCode.OK, StatusCode.Unimplemented)]
        [TestCase("application/json", (HttpStatusCode)418, null)]
        public async Task UnimplementedContentType_ReturnUnimplementedForAppGrpc(string contentType, HttpStatusCode httpStatusCode, StatusCode? grpcStatusCode)
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "HasMapped/Extra");
            httpRequest.Content = new StreamContent(requestStream);
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(httpStatusCode, response.StatusCode);

            Assert.AreEqual(grpcStatusCode?.ToTrailerString() ?? string.Empty, Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.StatusTrailer].ToString());
        }
    }
}
