using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Text;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class ResponseMessageContextTests
    {
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void CanCompress_WithNoWriteOptions_AllowsIfEncodingSet(bool setEncoding, bool expectedCanCompressResult)
        {
            // Arrange
            WriteOptions? writeOptions = null;

            // Act
            var responseMessageContext = CreateResponseMessageContext( writeOptions, setEncoding: setEncoding);
            bool canCompress = responseMessageContext.CanCompress();

            // Assert
            Assert.AreEqual(expectedCanCompressResult, canCompress);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void CanCompress_WithNoCompressSet_DoesNotAllow(bool setEncoding)
        {
            // Arrange
            WriteOptions? writeOptions = new WriteOptions(WriteFlags.NoCompress);

            // Act
            var responseMessageContext = CreateResponseMessageContext(writeOptions, setEncoding: setEncoding);
            bool canCompress = responseMessageContext.CanCompress();

            // Assert
            Assert.IsFalse(canCompress);
        }

        [TestCase(true, true)]
        [TestCase(false, false)]
        public void CanCompress_WithNoCompressNotSet_AllowsIfEncodingIsSet(bool setEncoding, bool expectedCanCompressResult)
        {
            // Arrange
            WriteOptions? writeOptions = new WriteOptions(WriteFlags.BufferHint);

            // Act
            var responseMessageContext = CreateResponseMessageContext(writeOptions, setEncoding: setEncoding);
            bool canCompress = responseMessageContext.CanCompress();

            // Assert
            Assert.AreEqual(expectedCanCompressResult, canCompress);
        }


        private ResponseMessageContext CreateResponseMessageContext(WriteOptions? writeOptions, bool setEncoding)
        {
            var httpContext = new DefaultHttpContext();

            HttpContextServerCallContext serverCallContext;
            if (setEncoding)
            {
                ICompressionProvider compressionProvider = new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest);
                var compressionProviders = new List<ICompressionProvider>
                {
                    compressionProvider
                };

                httpContext.Request.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, compressionProvider.EncodingName);
                httpContext.Request.Headers.Add(GrpcProtocolConstants.MessageAcceptEncodingHeader, compressionProvider.EncodingName);
                serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext, compressionProviders, compressionProvider.EncodingName);
            }
            else
            {
                serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext);
            }

            serverCallContext.WriteOptions = writeOptions;
            return serverCallContext.ResponseMessageContext;
        }
    }
}
