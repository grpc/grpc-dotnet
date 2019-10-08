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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Compression;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class CompressionTests
    {
        [Test]
        public async Task AsyncUnaryCall_UnknownCompressMetadataSentWithRequest_ThrowsError()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestStream = await request.Content.ReadAsStreamAsync();

                helloRequest = await StreamExtensions.ReadMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    maximumMessageSize: null,
                    GrpcProtocolConstants.DefaultCompressionProviders,
                    singleMessage: true,
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
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
            Assert.AreEqual("Error starting gRPC call: Could not find compression provider for 'not-supported'.", ex.Status.Detail);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task AsyncUnaryCall_CompressMetadataSentWithRequest_RequestMessageCompressed(bool compressionDisabledOnOptions)
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;
            bool? isRequestNotCompressed = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestData = await request.Content.ReadAsByteArrayAsync();
                isRequestNotCompressed = requestData[0] == 0;

                helloRequest = await StreamExtensions.ReadMessageAsync(
                    new MemoryStream(requestData),
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    maximumMessageSize: null,
                    GrpcProtocolConstants.DefaultCompressionProviders,
                    singleMessage: true,
                    CancellationToken.None);

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var compressionProviders = GrpcProtocolConstants.DefaultCompressionProviders.Values.ToList();
            compressionProviders.Add(new TestCompressionProvider());

            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.CompressionProviders = compressionProviders);

            var compressionMetadata = CreateClientCompressionMetadata("gzip");
            var callOptions = new CallOptions(headers: compressionMetadata);
            if (compressionDisabledOnOptions)
            {
                callOptions = callOptions.WithWriteOptions(new WriteOptions(WriteFlags.NoCompress));
            }

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, callOptions, new HelloRequest
            {
                Name = "Hello"
            });

            // Assert
            var response = await call.ResponseAsync;
            Assert.IsNotNull(response);
            Assert.AreEqual("Hello world", response.Message);

            Debug.Assert(httpRequestMessage != null);
            Assert.AreEqual("identity,gzip,test", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());
            Assert.AreEqual("gzip", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());
            Assert.AreEqual(false, httpRequestMessage.Headers.Contains(GrpcProtocolConstants.CompressionRequestAlgorithmHeader));

            Debug.Assert(helloRequest != null);
            Assert.AreEqual("Hello", helloRequest.Name);

            Assert.AreEqual(compressionDisabledOnOptions, isRequestNotCompressed);
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

                helloRequest = await StreamExtensions.ReadMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    maximumMessageSize: null,
                    GrpcProtocolConstants.DefaultCompressionProviders,
                    singleMessage: true,
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
        public async Task AsyncUnaryCall_CompressedResponseWithUnknownEncoding_ErrorThrown()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestStream = await request.Content.ReadAsStreamAsync();

                helloRequest = await StreamExtensions.ReadMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    maximumMessageSize: null,
                    GrpcProtocolConstants.DefaultCompressionProviders,
                    singleMessage: true,
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
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unimplemented, ex.StatusCode);
            Assert.AreEqual("Unsupported grpc-encoding value 'not-supported'. Supported encodings: identity, gzip", ex.Status.Detail);
        }

        [Test]
        public async Task AsyncClientStreamingCall_CompressMetadataSentWithRequest_RequestMessageCompressed()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;
            HelloRequest? helloRequest1 = null;
            HelloRequest? helloRequest2 = null;
            bool? isRequestNotCompressed1 = null;
            bool? isRequestNotCompressed2 = null;

            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var requestData = await request.Content.ReadAsByteArrayAsync();
                var requestStream = new MemoryStream(requestData);

                isRequestNotCompressed1 = requestData[0] == 0;
                helloRequest1 = await StreamExtensions.ReadMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    maximumMessageSize: null,
                    GrpcProtocolConstants.DefaultCompressionProviders,
                    singleMessage: false,
                    CancellationToken.None);

                isRequestNotCompressed2 = requestData[requestStream.Position] == 0;
                helloRequest2 = await StreamExtensions.ReadMessageAsync(
                    requestStream,
                    NullLogger.Instance,
                    ClientTestHelpers.ServiceMethod.RequestMarshaller.ContextualDeserializer,
                    "gzip",
                    maximumMessageSize: null,
                    GrpcProtocolConstants.DefaultCompressionProviders,
                    singleMessage: false,
                    CancellationToken.None);

                var reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var compressionProviders = GrpcProtocolConstants.DefaultCompressionProviders.Values.ToList();
            compressionProviders.Add(new TestCompressionProvider());

            var invoker = HttpClientCallInvokerFactory.Create(httpClient, configure: o => o.CompressionProviders = compressionProviders);

            var compressionMetadata = CreateClientCompressionMetadata("gzip");
            var callOptions = new CallOptions(headers: compressionMetadata);

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, callOptions);

            await call.RequestStream.WriteAsync(new HelloRequest
            {
                Name = "Hello One"
            }).DefaultTimeout();

            call.RequestStream.WriteOptions = new WriteOptions(WriteFlags.NoCompress);
            await call.RequestStream.WriteAsync(new HelloRequest
            {
                Name = "Hello Two"
            }).DefaultTimeout();

            await call.RequestStream.CompleteAsync();

            // Assert
            var response = await call.ResponseAsync.DefaultTimeout();
            Assert.IsNotNull(response);
            Assert.AreEqual("Hello world", response.Message);

            Debug.Assert(httpRequestMessage != null);
            Assert.AreEqual("identity,gzip,test", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());
            Assert.AreEqual("gzip", httpRequestMessage.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());
            Assert.AreEqual(false, httpRequestMessage.Headers.Contains(GrpcProtocolConstants.CompressionRequestAlgorithmHeader));

            Debug.Assert(helloRequest1 != null);
            Assert.AreEqual("Hello One", helloRequest1.Name);
            Debug.Assert(helloRequest2 != null);
            Assert.AreEqual("Hello Two", helloRequest2.Name);

            Assert.IsFalse(isRequestNotCompressed1);
            Assert.IsTrue(isRequestNotCompressed2);
        }

        private static Metadata CreateClientCompressionMetadata(string algorithmName)
        {
            return new Metadata
            {
                { new Metadata.Entry(GrpcProtocolConstants.CompressionRequestAlgorithmHeader, algorithmName) }
            };
        }

        private class TestCompressionProvider : ICompressionProvider
        {
            public string EncodingName => "test";

            public Stream CreateCompressionStream(Stream stream, System.IO.Compression.CompressionLevel? compressionLevel)
            {
                throw new NotImplementedException();
            }

            public Stream CreateDecompressionStream(Stream stream)
            {
                throw new NotImplementedException();
            }
        }
    }
}
