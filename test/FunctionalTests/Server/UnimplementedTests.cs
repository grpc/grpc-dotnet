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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
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

            var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/MethodDoesNotExist");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            response.AssertTrailerStatus(StatusCode.Unimplemented, "Method is unimplemented.");
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

            var httpRequest = GrpcHttpHelper.Create("Greet.ServiceDoesNotExist/Method");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            response.AssertTrailerStatus(StatusCode.Unimplemented, "Service is unimplemented.");
        }

        [TestCase("application/grpc", "POST", HttpStatusCode.OK, StatusCode.Unimplemented)]
        [TestCase("application/grpc", "GET", (HttpStatusCode)404, null)]
        [TestCase("application/json", "POST", (HttpStatusCode)404, null)]
        [TestCase("application/json", "GET", (HttpStatusCode)404, null)]
        public async Task UnimplementedContentType_ReturnUnimplementedForAppGrpc(string contentType, string httpMethod, HttpStatusCode httpStatusCode, StatusCode? grpcStatusCode)
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create("HasMapped/Extra");
            httpRequest.Method = new HttpMethod(httpMethod);
            httpRequest.Content = new StreamContent(requestStream);
            httpRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(httpStatusCode, response.StatusCode);
            if (grpcStatusCode != null)
            {
                Assert.AreEqual(grpcStatusCode.Value.ToTrailerString(), response.Headers.GetValues(GrpcProtocolConstants.StatusTrailer).Single());
            }
            else
            {
                Assert.IsFalse(response.Headers.TryGetValues(GrpcProtocolConstants.StatusTrailer, out _));
                Assert.IsFalse(response.TrailingHeaders.TryGetValues(GrpcProtocolConstants.StatusTrailer, out _));
            }
        }
    }
}
