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

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FunctionalTestsWebsite.Services;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Compression;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class CompressionTests : FunctionalTestBase
    {
        public async Task SendCompressedMessage_ServiceHasNoCompressionConfigured_ResponseIdentityEncoding()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage, "gzip");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "gzip");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task SendCompressedMessageWithIdentity_ReturnInternalError()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(GreeterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'Internal' raised.";
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage, "gzip");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "identity");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            response.AssertTrailerStatus(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");
        }

        [Test]
        public async Task SendUnsupportedEncodingHeaderWithUncompressedMessage_ReturnUncompressedMessage()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "DOES_NOT_EXIST");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task SendCompressedMessageWithUnsupportedEncoding_ReturnUnimplemented()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(GreeterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'Unimplemented' raised.";
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(
                requestStream,
                requestMessage,
                "DOES_NOT_EXIST",
                new List<ICompressionProvider>
                {
                    new DoesNotExistCompressionProvider()
                });

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "DOES_NOT_EXIST");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("gzip", response.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());

            response.AssertTrailerStatus(StatusCode.Unimplemented, "Unsupported grpc-encoding value 'DOES_NOT_EXIST'. Supported encodings: gzip");
        }

        private class DoesNotExistCompressionProvider : ICompressionProvider
        {
            public string EncodingName => "DOES_NOT_EXIST";

            public Stream CreateCompressionStream(Stream stream, System.IO.Compression.CompressionLevel? compressionLevel)
            {
                return stream;
            }

            public Stream CreateDecompressionStream(Stream stream)
            {
                return stream;
            }
        }

        [Test]
        public async Task SendCompressedMessageWithoutEncodingHeader_ServerErrorResponse()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(GreeterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'Internal' raised.";
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage, "gzip");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Greet.Greeter/SayHello");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            response.AssertTrailerStatus(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");
        }

        [Test]
        public async Task SendCompressedMessageAndReturnResultWithNoCompressFlag_ResponseNotCompressed()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage, "gzip");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Compression.CompressionService/WriteMessageWithoutCompression");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "gzip");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, "gzip");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

            // The overall encoding is gzip but the actual response does not use compression
            Assert.AreEqual("gzip", response.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task SendUncompressedMessageToServiceWithCompression_ResponseCompressed()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Compression.CompressionService/SayHello");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, "gzip");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("gzip", response.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout(), "gzip");
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task SendIdentityGrpcAcceptEncodingToServiceWithCompression_ResponseUncompressed()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Compression.CompressionService/SayHello");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "identity");
            httpRequest.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, "identity");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("identity", response.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());

            var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }
    }
}
