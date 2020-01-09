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
using System.Linq;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.AspNetCore.Web;
using Grpc.AspNetCore.Web.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class GrpcWebMiddlewareTests
    {
        [Test]
        public async Task Invoke_NonGrpcWebContentType_NotProcessed()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var middleware = CreateMiddleware(logger: testLoggerFactory.CreateLogger<GrpcWebMiddleware>());
            var httpContext = new DefaultHttpContext();

            // Act
            await middleware.Invoke(httpContext);

            // Assert
            Assert.AreEqual(0, testSink.Writes.Count);
            Assert.IsNull(httpContext.Features.Get<IHttpResponseTrailersFeature>());
        }

        [TestCase(GrpcWebProtocolConstants.GrpcWebContentType, nameof(ServerGrpcWebMode.GrpcWeb))]
        [TestCase(GrpcWebProtocolConstants.GrpcWebContentType + "+proto", nameof(ServerGrpcWebMode.GrpcWeb))]
        [TestCase(GrpcWebProtocolConstants.GrpcWebTextContentType, nameof(ServerGrpcWebMode.GrpcWebText))]
        [TestCase(GrpcWebProtocolConstants.GrpcWebTextContentType + "+proto", nameof(ServerGrpcWebMode.GrpcWebText))]
        [TestCase(GrpcWebProtocolConstants.GrpcContentType, nameof(ServerGrpcWebMode.None))]
        [TestCase("application/json", nameof(ServerGrpcWebMode.None))]
        [TestCase("", nameof(ServerGrpcWebMode.None))]
        public void GetGrpcWebMode_ContentTypes_Matched(string contentType, string expectedGrpcWebMode)
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = contentType;

            // Act
            var grpcWebMode = GrpcWebMiddleware.GetGrpcWebMode(httpContext);

            // Assert
            Assert.AreEqual(Enum.Parse<ServerGrpcWebMode>(expectedGrpcWebMode), grpcWebMode);
        }

        [Test]
        public async Task Invoke_GrpcWebContentTypeAndNotEnabled_NotProcessed()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var middleware = CreateMiddleware(logger: testLoggerFactory.CreateLogger<GrpcWebMiddleware>());
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = GrpcWebProtocolConstants.GrpcWebContentType;

            // Act
            await middleware.Invoke(httpContext);

            // Assert
            Assert.IsNull(httpContext.Features.Get<IHttpResponseTrailersFeature>());

            Assert.AreEqual(2, testSink.Writes.Count);
            var writes = testSink.Writes.ToList();
            Assert.AreEqual("DetectedGrpcWebRequest", writes[0].EventId.Name);
            Assert.AreEqual("GrpcWebRequestNotProcessed", writes[1].EventId.Name);
        }

        [Test]
        public async Task Invoke_GrpcWebContentTypeAndEnabled_Processed()
        {
            // Arrange
            var testSink = new TestSink();
            var testLoggerFactory = new TestLoggerFactory(testSink, true);

            var middleware = CreateMiddleware(
                options: new GrpcWebOptions { GrpcWebEnabled = true },
                logger: testLoggerFactory.CreateLogger<GrpcWebMiddleware>());
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = GrpcWebProtocolConstants.GrpcWebContentType;

            // Act
            await middleware.Invoke(httpContext);

            // Assert
            Assert.AreEqual(1, testSink.Writes.Count);
            var writes = testSink.Writes.ToList();
            Assert.AreEqual("DetectedGrpcWebRequest", writes[0].EventId.Name);
        }

        [Test]
        public async Task Invoke_GrpcWebContentTypeAndMetadata_Processed()
        {
            // Arrange
            var middleware = CreateMiddleware(options: new GrpcWebOptions { GrpcWebEnabled = false });
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Protocol = "HTTP/1.1";
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = GrpcWebProtocolConstants.GrpcWebContentType;
            httpContext.SetEndpoint(new Endpoint(c => Task.CompletedTask, new EndpointMetadataCollection(new EnableGrpcWebAttribute()), string.Empty));

            var testHttpResponseFeature = new TestHttpResponseFeature();
            httpContext.Features.Set<IHttpResponseFeature>(testHttpResponseFeature);

            // Act 1
            await middleware.Invoke(httpContext);

            // Assert 1
            Assert.AreEqual(GrpcWebProtocolConstants.GrpcContentType, httpContext.Request.ContentType);
            Assert.AreEqual(GrpcWebProtocolConstants.Http2Protocol, httpContext.Request.Protocol);
            Assert.AreEqual(1, testHttpResponseFeature.StartingCallbackCount);

            // Act 2
            httpContext.Response.ContentType = GrpcWebProtocolConstants.GrpcContentType;

            var c = testHttpResponseFeature.StartingCallbacks[0];
            await c.callback(c.state);

            // Assert 2
            Assert.AreEqual("HTTP/1.1", httpContext.Request.Protocol);
            Assert.AreEqual(GrpcWebProtocolConstants.GrpcWebContentType, httpContext.Response.ContentType);
        }

        private static GrpcWebMiddleware CreateMiddleware(
            GrpcWebOptions? options = null,
            ILogger<GrpcWebMiddleware>? logger = null)
        {
            return new GrpcWebMiddleware(
                Options.Create<GrpcWebOptions>(options ?? new GrpcWebOptions()),
                logger ?? NullLogger<GrpcWebMiddleware>.Instance,
                c => Task.CompletedTask);
        }
    }
}
