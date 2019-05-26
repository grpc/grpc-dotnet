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
using System.Net.Http;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class HttpContextTests : FunctionalTestBase
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

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHelloWithHttpContextAccessor?query=extra");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus();
            Assert.AreEqual("/Greet.Greeter/SayHelloWithHttpContextAccessor?query=extra", response.TrailingHeaders.GetValues("Test-HttpContext-PathAndQueryString").Single());
        }

        [Test]
        public async Task HttpContextExtensionMethod_ReturnContextInTrailer()
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>((request, context) =>
            {
                var httpContext = context.GetHttpContext();
                context.ResponseTrailers.Add("Test-HttpContext-PathAndQueryString", httpContext.Request.Path + httpContext.Request.QueryString);

                return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{url}?query=extra");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus();
            Assert.AreEqual($"{url}?query=extra", response.TrailingHeaders.GetValues("Test-HttpContext-PathAndQueryString").Single());
        }
    }
}
