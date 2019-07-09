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
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Internal.Compression;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class CompressionTests
    {
        [Test]
        public void AsyncUnaryCall_UnknownCompressMetadataSentWithRequest_ThrowsError()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestStream = await request.Content.ReadAsStreamAsync();

                helloRequest = await StreamExtensions.ReadSingleMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    CancellationToken.None);

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var compressionMetadata = CreateClientCompressionMetadata("not-supported");
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(headers: compressionMetadata), new HelloRequest
            {
                Name = "Hello"
            });

            // Assert
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseAsync.DefaultTimeout());
            Assert.AreEqual("Could not find compression provider for 'not-supported'.", ex.Message);
        }

        [Test]
        public async Task AsyncUnaryCall_CompressMetadataSentWithRequest_RequestMessageCompressed()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestStream = await request.Content.ReadAsStreamAsync();

                helloRequest = await StreamExtensions.ReadSingleMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    CancellationToken.None);

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var compressionMetadata = CreateClientCompressionMetadata("gzip");
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(headers: compressionMetadata), new HelloRequest
            {
                Name = "Hello"
            });

            // Assert
            var response = await call.ResponseAsync;
            Assert.IsNotNull(response);
            Assert.AreEqual("Hello world", response.Message);

            Debug.Assert(httpRequestMessage != null);
            Assert.AreEqual("gzip", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());
            Assert.AreEqual(false, httpRequestMessage.Headers.Contains(GrpcProtocolConstants.CompressionRequestAlgorithmHeader));

            Debug.Assert(helloRequest != null);
            Assert.AreEqual("Hello", helloRequest.Name);
        }

        [Test]
        public async Task AsyncUnaryCall_CompressedResponse_ResponseMessageDecompressed()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestStream = await request.Content.ReadAsStreamAsync();

                helloRequest = await StreamExtensions.ReadSingleMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    CancellationToken.None);

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var compressionProvider = new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest);
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply, compressionProvider).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcEncoding: "gzip");
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = "Hello"
            });

            // Assert
            var response = await call.ResponseAsync;
            Assert.IsNotNull(response);
            Assert.AreEqual("Hello world", response.Message);
        }

        [Test]
        public void AsyncUnaryCall_CompressedResponseWithUnknownEncoding_ErrorThrown()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestStream = await request.Content.ReadAsStreamAsync();

                helloRequest = await StreamExtensions.ReadSingleMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    CancellationToken.None);

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var compressionProvider = new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest);
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply, compressionProvider).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcEncoding: "not-supported");
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest
            {
                Name = "Hello"
            });

            // Assert
            var ex = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync.DefaultTimeout());
            Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
            Assert.AreEqual("Unsupported grpc-encoding value 'not-supported'. Supported encodings: gzip", ex.Status.Detail);
        }

        private static Metadata CreateClientCompressionMetadata(string algorithmName)
        {
            return new Metadata
            {
                { new Metadata.Entry(GrpcProtocolConstants.CompressionRequestAlgorithmHeader, algorithmName) }
            };
        }
    }
}
