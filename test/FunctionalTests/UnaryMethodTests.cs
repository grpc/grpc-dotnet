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
using Google.Protobuf.WellKnownTypes;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class UnaryMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task SendValidRequest_SuccessResponse()
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
                "Greet.Greeter/SayHello",
                new StreamContent(ms)).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);

            Assert.AreEqual(StatusCode.OK.ToTrailerString(), Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.StatusTrailer].Single());
        }

        [Test]
        public async Task StreamedMessage_SuccessResponseAfterMessageReceived()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            var response = await responseTask.DefaultTimeout();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);
        }

        [Test]
        public async Task AdditionalDataAfterStreamedMessage_ErrorResponse()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();

            // TODO - this should return a response with a gRPC status object
            var ex = Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                await responseTask.DefaultTimeout();
            });
            Assert.AreEqual("Additional data after the message received.", ex.Message);
        }

        [Test]
        public async Task MessageSentInMultipleChunks_SuccessResponse()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Content = new StreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            // Send message one byte at a time then finish
            foreach (var b in ms.ToArray())
            {
                await requestStream.AddDataAndWait(new[] { b }).DefaultTimeout();
            }
            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            var response = await responseTask.DefaultTimeout();

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);

            Assert.AreEqual(StatusCode.OK.ToTrailerString(), Fixture.TrailersContainer.Trailers[GrpcProtocolConstants.StatusTrailer].Single());
        }

        public Task<HelloReply> ReturnHeadersTwice(HelloRequest request, ServerCallContext context)
        {
            context.WriteResponseHeadersAsync(null);

            try
            {
                context.WriteResponseHeadersAsync(null);
            }
            catch (InvalidOperationException e) when (e.Message == "Response headers can only be sent once per call.")
            {
                return Task.FromResult(new HelloReply { Message = "Exception validated" });
            }

            return Task.FromResult(new HelloReply { Message = "No exception thrown" });
        }

        [Test]
        public async Task SendHeadersTwice_ThrowsException()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var url = Fixture.DynamicGrpc.AddUnaryMethod<UnaryMethodTests, HelloRequest, HelloReply>(nameof(ReturnHeadersTwice));

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new StreamContent(ms)).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Exception validated", responseMessage.Message);
        }

        public Task<Empty> ReturnContextInfoInTrailers(Empty request, ServerCallContext context)
        {
            context.ResponseTrailers.Add("Test-Method", context.Method);
            context.ResponseTrailers.Add("Test-Peer", context.Peer ?? string.Empty); // null because there is not a remote ip address
            context.ResponseTrailers.Add("Test-Host", context.Host);

            return Task.FromResult(new Empty());
        }

        [Test]
        public async Task ValidRequest_ReturnContextInfoInTrailers()
        {
            // Arrange
            var requestMessage = new Empty();

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var url = Fixture.DynamicGrpc.AddUnaryMethod<UnaryMethodTests, Empty, Empty>(nameof(ReturnContextInfoInTrailers));

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new StreamContent(ms)).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues("grpc-encoding").Single());
            Assert.AreEqual("application/grpc", response.Content.Headers.ContentType.MediaType);

            var responseMessage = MessageHelpers.AssertReadMessage<Empty>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.IsNotNull(responseMessage);

            Assert.AreEqual("/UnaryMethodTests/ReturnContextInfoInTrailers", Fixture.TrailersContainer.Trailers["Test-Method"].ToString());
            Assert.AreEqual(string.Empty, Fixture.TrailersContainer.Trailers["Test-Peer"].ToString());
            Assert.AreEqual("localhost", Fixture.TrailersContainer.Trailers["Test-Host"].ToString());
        }
    }
}
