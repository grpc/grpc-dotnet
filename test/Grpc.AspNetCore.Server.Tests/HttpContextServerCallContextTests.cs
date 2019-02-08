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
using System.Net;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class HttpContextServerCallContextTests
    {
        [TestCase("127.0.0.1", 50051, "ipv4:127.0.0.1:50051")]
        [TestCase("::1", 50051, "ipv6:::1:50051")]
        public void Peer_FormatsRemoteAddressCorrectly(string ipAddress, int port, string expected)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ipAddress);
            httpContext.Connection.RemotePort = port;

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(expected, serverCallContext.Peer);
        }

        [Test]
        public async Task WriteResponseHeadersAsyncCore_AddsMetadataToResponseHeaders()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var metadata = new Metadata();
            metadata.Add("foo", "bar");

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            await serverCallContext.WriteResponseHeadersAsync(metadata);

            // Assert
            Assert.AreEqual("bar", httpContext.Response.Headers["foo"]);
        }

        [TestCase("foo-bin")]
        [TestCase("Foo-Bin")]
        [TestCase("FOO-BIN")]
        public async Task WriteResponseHeadersAsyncCore_Base64EncodesBinaryResponseHeaders(string headerName)
        {
            // Arrange
            var headerBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var httpContext = new DefaultHttpContext();
            var metadata = new Metadata();
            metadata.Add(headerName, headerBytes);

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            await serverCallContext.WriteResponseHeadersAsync(metadata);

            // Assert
            CollectionAssert.AreEqual(headerBytes, Convert.FromBase64String(httpContext.Response.Headers["foo-bin"].ToString()));
        }

        [TestCase("name-suffix", "value", "name-suffix", "value")]
        [TestCase("Name-Suffix", "Value", "name-suffix", "Value")]
        public void RequestHeaders_LowercasesHeaderNames(string headerName, string headerValue, string expectedHeaderName, string expectedHeaderValue)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[headerName] = headerValue;

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(1, serverCallContext.RequestHeaders.Count);
            var header = serverCallContext.RequestHeaders[0];
            Assert.AreEqual(expectedHeaderName, header.Key);
            Assert.AreEqual(expectedHeaderValue, header.Value);
        }

        [TestCase(":method")]
        [TestCase(":scheme")]
        [TestCase(":authority")]
        [TestCase(":path")]
        public void RequestHeaders_IgnoresPseudoHeaders(string headerName)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[headerName] = "dummy";

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(0, serverCallContext.RequestHeaders.Count);
        }

        [TestCase("test-bin")]
        [TestCase("Test-Bin")]
        [TestCase("TEST-BIN")]
        public void RequestHeaders_ParsesBase64EncodedBinaryHeaders(string headerName)
        {
            var headerBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[headerName] = Convert.ToBase64String(headerBytes);

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);

            // Assert
            Assert.AreEqual(1, serverCallContext.RequestHeaders.Count);
            var header = serverCallContext.RequestHeaders[0];
            Assert.True(header.IsBinary);
            CollectionAssert.AreEqual(headerBytes, header.ValueBytes);
        }

        [Test]
        public void RequestHeaders_ThrowsForNonBase64EncodedBinaryHeader()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["test-bin"] = "a;b";

            // Act
            var serverCallContext = new HttpContextServerCallContext(httpContext);

            // Assert
            Assert.Throws<FormatException>(() => serverCallContext.RequestHeaders.Clear());
        }

        [TestCase("trailer-name", "trailer-value", "trailer-name", "trailer-value")]
        [TestCase("Trailer-Name", "Trailer-Value", "trailer-name", "Trailer-Value")]
        public void ConsolidateTrailers_LowercaseTrailerNames(string trailerName, string trailerValue, string expectedTrailerName, string expectedTrailerValue)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(trailerName, trailerValue);

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(expectedTrailerValue, responseTrailers[expectedTrailerName].ToString());
            Assert.AreEqual("0", responseTrailers[GrpcProtocolConstants.StatusTrailer]);
        }

        public void ConsolidateTrailers_AppendsStatus()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            serverCallContext.Status = new Status(StatusCode.Internal, "Error message");

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual("Error message", responseTrailers[GrpcProtocolConstants.MessageTrailer]);
        }

        public void ConsolidateTrailers_StatusOverwritesTrailers()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(GrpcProtocolConstants.StatusTrailer, StatusCode.OK.ToString("D"));
            serverCallContext.ResponseTrailers.Add(GrpcProtocolConstants.MessageTrailer, "All is good");
            serverCallContext.Status = new Status(StatusCode.Internal, "Error message");

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.Internal.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual("Error message", responseTrailers[GrpcProtocolConstants.MessageTrailer]);
        }

        [TestCase("trailer-bin")]
        [TestCase("Trailer-Bin")]
        [TestCase("TRAILER-BIN")]
        public void ConsolidateTrailers_Base64EncodesBinaryTrailers(string trailerName)
        {
            // Arrange
            var trailerBytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
            var httpContext = new DefaultHttpContext();
            httpContext.Features.Set<IHttpResponseTrailersFeature>(new TestHttpResponseTrailersFeature());
            var serverCallContext = new HttpContextServerCallContext(httpContext);
            serverCallContext.ResponseTrailers.Add(trailerName, trailerBytes);

            // Act
            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Assert
            var responseTrailers = httpContext.Features.Get<IHttpResponseTrailersFeature>().Trailers;

            Assert.AreEqual(2, responseTrailers.Count);
            Assert.AreEqual(StatusCode.OK.ToString("D"), responseTrailers[GrpcProtocolConstants.StatusTrailer]);
            Assert.AreEqual(Convert.ToBase64String(trailerBytes), responseTrailers["trailer-bin"]);
        }

        private class TestHttpResponseTrailersFeature : IHttpResponseTrailersFeature
        {
            public IHeaderDictionary Trailers { get; set; } = new HttpResponseTrailers();
        }
    }
}
