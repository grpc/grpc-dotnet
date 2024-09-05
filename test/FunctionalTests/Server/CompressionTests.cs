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

using System.IO.Pipelines;
using System.Net;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server;

[TestFixture]
public class CompressionTests : FunctionalTestBase
{
    [Test]
    public async Task SendCompressedMessage_UnaryEnabledInCallWithInvalidSetting_UncompressedMessageReturned()
    {
        async Task<HelloReply> UnaryEnableCompression(HelloRequest request, ServerCallContext context)
        {
            var headers = new Metadata { new Metadata.Entry("grpc-internal-encoding-request", "PURPLE_MONKEY_DISHWASHER") };
            await context.WriteResponseHeadersAsync(headers);

            return new HelloReply { Message = "Hello " + request.Name };
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryEnableCompression);

        var ms = new MemoryStream();
        MessageHelpers.WriteMessage(ms, new HelloRequest
        {
            Name = "World"
        });

        var httpRequest = GrpcHttpHelper.Create(method.FullName);
        httpRequest.Content = new PushStreamContent(
            async s =>
            {
                await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                await s.FlushAsync().DefaultTimeout();
            });

        // Act
        var responseTask = Fixture.Client.SendAsync(httpRequest);

        // Assert
        var response = await responseTask.DefaultTimeout();

        response.AssertIsSuccessfulGrpcRequest();

        // Because the client didn't send this encoding in accept, the server has sent the message uncompressed.
        Assert.AreEqual("PURPLE_MONKEY_DISHWASHER", response.Headers.GetValues("grpc-encoding").Single());

        var returnedMessageData = await response.Content.ReadAsByteArrayAsync().DefaultTimeout();
        Assert.AreEqual(0, returnedMessageData[0]);

        var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(returnedMessageData);
        Assert.AreEqual("Hello World", responseMessage.Message);
        response.AssertTrailerStatus();
    }

    [TestCase("gzip")]
    [TestCase("deflate")]
    public async Task SendCompressedMessage_UnaryEnabledInCall_CompressedMessageReturned(string algorithmName)
    {
        async Task<HelloReply> UnaryEnableCompression(HelloRequest request, ServerCallContext context)
        {
            var headers = new Metadata { new Metadata.Entry("grpc-internal-encoding-request", algorithmName) };
            await context.WriteResponseHeadersAsync(headers);

            return new HelloReply { Message = "Hello " + request.Name };
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryEnableCompression);

        var ms = new MemoryStream();
        MessageHelpers.WriteMessage(ms, new HelloRequest
        {
            Name = "World"
        });

        var httpRequest = GrpcHttpHelper.Create(method.FullName);
        httpRequest.Content = new PushStreamContent(
            async s =>
            {
                await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                await s.FlushAsync().DefaultTimeout();
            });

        // Act
        var responseTask = Fixture.Client.SendAsync(httpRequest);

        // Assert
        var response = await responseTask.DefaultTimeout();

        response.AssertIsSuccessfulGrpcRequest();

        Assert.AreEqual(algorithmName, response.Headers.GetValues("grpc-encoding").Single());

        var returnedMessageData = await response.Content.ReadAsByteArrayAsync().DefaultTimeout();
        Assert.AreEqual(1, returnedMessageData[0]);

        var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(returnedMessageData, algorithmName);
        Assert.AreEqual("Hello World", responseMessage.Message);
        response.AssertTrailerStatus();
    }

    [TestCase("gzip")]
    [TestCase("deflate")]
    public async Task SendCompressedMessage_ServerStreamingEnabledInCall_CompressedMessageReturned(string algorithmName)
    {
        async Task ServerStreamingEnableCompression(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            var headers = new Metadata { new Metadata.Entry("grpc-internal-encoding-request", algorithmName) };
            await context.WriteResponseHeadersAsync(headers);

            await responseStream.WriteAsync(new HelloReply { Message = "Hello 1" });

            responseStream.WriteOptions = new WriteOptions(WriteFlags.NoCompress);
            await responseStream.WriteAsync(new HelloReply { Message = "Hello 2" });
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(ServerStreamingEnableCompression);

        var ms = new MemoryStream();
        MessageHelpers.WriteMessage(ms, new HelloRequest
        {
            Name = "World"
        });

        var httpRequest = GrpcHttpHelper.Create(method.FullName);
        httpRequest.Content = new PushStreamContent(
            async s =>
            {
                await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                await s.FlushAsync().DefaultTimeout();
            });

        // Act
        var responseTask = Fixture.Client.SendAsync(httpRequest);

        // Assert
        var response = await responseTask.DefaultTimeout();

        response.AssertIsSuccessfulGrpcRequest();

        Assert.AreEqual(algorithmName, response.Headers.GetValues("grpc-encoding").Single());

        var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
        var pipeReader = PipeReader.Create(responseStream);

        ReadResult readResult;

        readResult = await pipeReader.ReadAsync().AsTask().DefaultTimeout();
        Assert.AreEqual(1, readResult.Buffer.FirstSpan[0]); // Message is compressed
        var greeting1 = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader, algorithmName).DefaultTimeout();
        Assert.AreEqual($"Hello 1", greeting1!.Message);

        readResult = await pipeReader.ReadAsync().AsTask().DefaultTimeout();
        Assert.AreEqual(0, readResult.Buffer.FirstSpan[0]); // Message is uncompressed
        var greeting2 = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader, algorithmName).DefaultTimeout();
        Assert.AreEqual($"Hello 2", greeting2!.Message);

        var finishedTask = MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader);
        Assert.IsNull(await finishedTask.DefaultTimeout());
    }

    [TestCase("gzip")]
    [TestCase("deflate")]
    public async Task SendCompressedMessage_ServiceHasNoCompressionConfigured_ResponseIdentityEncoding(string algorithmName)
    {
        // Arrange
        var requestMessage = new HelloRequest
        {
            Name = "World"
        };

        var requestStream = new MemoryStream();
        MessageHelpers.WriteMessage(requestStream, requestMessage, algorithmName);

        var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, algorithmName);
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains(GrpcProtocolConstants.MessageEncodingHeader));

        var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
        Assert.AreEqual("Hello World", responseMessage.Message);
        response.AssertTrailerStatus();
    }

    [TestCase("gzip")]
    [TestCase("deflate")]
    public async Task SendCompressedMessageWithIdentity_ReturnInternalError(string algorithmName)
    {
        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                writeContext.EventId.Name == "ErrorReadingMessage" &&
                writeContext.State.ToString() == "Error reading message.")
            {
                return true;
            }

            return false;
        });

        var requestMessage = new HelloRequest
        {
            Name = "World"
        };

        var requestStream = new MemoryStream();
        MessageHelpers.WriteMessage(requestStream, requestMessage, algorithmName);

        var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "identity");
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        response.AssertTrailerStatus(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");

        AssertHasLogRpcConnectionError(StatusCode.Internal, "Request sent 'identity' grpc-encoding value with compressed message.");
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

        var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
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
        var expectedError = "Unsupported grpc-encoding value 'DOES_NOT_EXIST'. Supported encodings: identity, gzip, deflate";

        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                writeContext.EventId.Name == "ErrorReadingMessage" &&
                writeContext.State.ToString() == "Error reading message." &&
                GetRpcExceptionDetail(writeContext.Exception) == expectedError)
            {
                return true;
            }

            return false;
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

        var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "DOES_NOT_EXIST");
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.AreEqual("identity,gzip,deflate", response.Headers.GetValues(GrpcProtocolConstants.MessageAcceptEncodingHeader).Single());

        response.AssertTrailerStatus(StatusCode.Unimplemented, expectedError);

        AssertHasLogRpcConnectionError(StatusCode.Unimplemented, expectedError);
    }

    private class DoesNotExistCompressionProvider : ICompressionProvider
    {
        public string EncodingName => "DOES_NOT_EXIST";

        public Stream CreateCompressionStream(Stream stream, System.IO.Compression.CompressionLevel? compressionLevel)
        {
            return new WrapperStream(stream);
        }

        public Stream CreateDecompressionStream(Stream stream)
        {
            return new WrapperStream(stream);
        }

        // Returned stream is disposed. Wrapper leaves the inner stream open.
        private class WrapperStream : Stream
        {
            private readonly Stream _innerStream;

            public WrapperStream(Stream innerStream)
            {
                _innerStream = innerStream;
            }

            public override bool CanRead => _innerStream.CanRead;
            public override bool CanSeek => _innerStream.CanSeek;
            public override bool CanWrite => _innerStream.CanWrite;
            public override long Length => _innerStream.Length;
            public override long Position
            {
                get => _innerStream.Position;
                set => _innerStream.Position = value;
            }

            public override void Flush() => _innerStream.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
            public override void SetLength(long value) => _innerStream.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        }
    }

    [TestCase("gzip")]
    [TestCase("deflate")]
    public async Task SendCompressedMessageWithoutEncodingHeader_ServerErrorResponse(string algorithmName)
    {
        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                writeContext.EventId.Name == "ErrorReadingMessage" &&
                writeContext.State.ToString() == "Error reading message." &&
                GetRpcExceptionDetail(writeContext.Exception) == "Request did not include grpc-encoding value with compressed message.")
            {
                return true;
            }

            return false;
        });

        var requestMessage = new HelloRequest
        {
            Name = "World"
        };

        var requestStream = new MemoryStream();
        MessageHelpers.WriteMessage(requestStream, requestMessage, algorithmName);

        var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        response.AssertTrailerStatus(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");

        AssertHasLogRpcConnectionError(StatusCode.Internal, "Request did not include grpc-encoding value with compressed message.");
    }

    [TestCase("gzip", "gzip", true)]
    [TestCase("gzip", "identity, gzip", true)]
    [TestCase("gzip", "gzip ", true)]
    [TestCase("deflate", "deflate", false)]
    public async Task SendCompressedMessageAndReturnResultWithNoCompressFlag_ResponseNotCompressed(string algorithmName, string messageAcceptEncoding, bool algorithmSupportedByServer)
    {
        // Arrange
        var requestMessage = new HelloRequest
        {
            Name = "World"
        };

        var requestStream = new MemoryStream();
        MessageHelpers.WriteMessage(requestStream, requestMessage, algorithmName);

        var httpRequest = GrpcHttpHelper.Create("Compression.CompressionService/WriteMessageWithoutCompression");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, algorithmName);
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, messageAcceptEncoding);
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        if (algorithmSupportedByServer)
        {
            // The overall encoding is gzip but the actual response does not use compression
            Assert.AreEqual(algorithmName, response.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());
        }
        else
        {
            Assert.IsFalse(response.Headers.Contains(GrpcProtocolConstants.MessageEncodingHeader));
        }

        var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
        Assert.AreEqual("Hello World", responseMessage.Message);
        response.AssertTrailerStatus();
    }

    [TestCase("gzip", true)]
    [TestCase("deflate", false)]
    public async Task SendUncompressedMessageToServiceWithCompression_ResponseCompressed(string algorithmName, bool algorithmSupportedByServer)
    {
        // Arrange
        var requestMessage = new HelloRequest
        {
            Name = "World"
        };

        var requestStream = new MemoryStream();
        MessageHelpers.WriteMessage(requestStream, requestMessage);

        var httpRequest = GrpcHttpHelper.Create("Compression.CompressionService/SayHello");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, algorithmName);
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        if (algorithmSupportedByServer)
        {
            Assert.AreEqual(algorithmName, response.Headers.GetValues(GrpcProtocolConstants.MessageEncodingHeader).Single());
        }
        else
        {
            Assert.IsFalse(response.Headers.Contains(GrpcProtocolConstants.MessageEncodingHeader));
        }

        var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(
            await response.Content.ReadAsByteArrayAsync().DefaultTimeout(),
            compressionEncoding: algorithmSupportedByServer ? algorithmName : null);
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

        var httpRequest = GrpcHttpHelper.Create("Compression.CompressionService/SayHello");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, "identity");
        httpRequest.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, "identity");
        httpRequest.Content = new GrpcStreamContent(requestStream);

        // Act
        var response = await Fixture.Client.SendAsync(httpRequest, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains(GrpcProtocolConstants.MessageEncodingHeader));

        var responseMessage = MessageHelpers.AssertReadMessage<HelloReply>(await response.Content.ReadAsByteArrayAsync().DefaultTimeout());
        Assert.AreEqual("Hello World", responseMessage.Message);
        response.AssertTrailerStatus();
    }
}
