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
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client.Web;
using Grpc.Net.Client.Web.Internal;
using Grpc.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Web.Tests
{
    [TestFixture]
    public class GrpcWebHandlerTests
    {
        [Test]
        public async Task HttpVersion_Unset_HttpRequestMessageVersionUnchanged()
        {
            // Arrange
            var request = new HttpRequestMessage
            {
                Version = GrpcWebProtocolConstants.Http2Version,
                Content = new ByteArrayContent(Array.Empty<byte>())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/grpc") }
                }
            };
            var testHttpHandler = new TestHttpHandler();
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb)
            {
                InnerHandler = testHttpHandler
            };
            var messageInvoker = new HttpMessageInvoker(grpcWebHandler);

            // Act
            var response = await messageInvoker.SendAsync(request, CancellationToken.None);

            // Assert
            Assert.AreEqual(GrpcWebProtocolConstants.Http2Version, testHttpHandler.Request!.Version);
            Assert.AreEqual(GrpcWebProtocolConstants.Http2Version, response.Version);
        }

        [Test]
        public async Task HttpVersion_Set_HttpRequestMessageVersionChanged()
        {
            // Arrange
            var request = new HttpRequestMessage
            {
                Version = GrpcWebProtocolConstants.Http2Version,
                Content = new ByteArrayContent(Array.Empty<byte>())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/grpc") }
                }
            };
            var testHttpHandler = new TestHttpHandler();
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb)
            {
                InnerHandler = testHttpHandler,
                HttpVersion = HttpVersion.Version11
            };
            var messageInvoker = new HttpMessageInvoker(grpcWebHandler);

            // Act
            var response = await messageInvoker.SendAsync(request, CancellationToken.None);

            // Assert
            Assert.AreEqual(HttpVersion.Version11, testHttpHandler.Request!.Version);
            Assert.AreEqual(GrpcWebProtocolConstants.Http2Version, response.Version);
        }

        [Test]
        public async Task SendAsync_GrpcCall_ResponseStreamingPropertySet()
        {
            // Arrange
            var request = new HttpRequestMessage
            {
                Version = GrpcWebProtocolConstants.Http2Version,
                Content = new ByteArrayContent(Array.Empty<byte>())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/grpc") }
                }
            };
            var testHttpHandler = new TestHttpHandler();
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, testHttpHandler);
            var messageInvoker = new HttpMessageInvoker(grpcWebHandler);

            // Act
            await messageInvoker.SendAsync(request, CancellationToken.None);

            // Assert
            Assert.AreEqual(true, testHttpHandler.WebAssemblyEnableStreamingResponse);
        }

        [Test]
        public async Task SendAsync_GrpcCallInBrowser_UserAgentFixed()
        {
            // Arrange
            var request = new HttpRequestMessage
            {
                Version = GrpcWebProtocolConstants.Http2Version,
                Content = new ByteArrayContent(Array.Empty<byte>())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/grpc") }
                }
            };
            request.Headers.TryAddWithoutValidation("User-Agent", "TestUserAgent");
            var testHttpHandler = new TestHttpHandler();
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, testHttpHandler);
            grpcWebHandler.OperatingSystem = new TestOperatingSystem
            {
                IsBrowser = true
            };
            var messageInvoker = new HttpMessageInvoker(grpcWebHandler);

            // Act
            await messageInvoker.SendAsync(request, CancellationToken.None);

            // Assert
            Assert.AreEqual(false, testHttpHandler.RequestHeaders!.TryGetValues("user-agent", out _));
            Assert.AreEqual(true, testHttpHandler.RequestHeaders!.TryGetValues("x-user-agent", out var values));
            Assert.AreEqual("TestUserAgent", values!.Single());
        }

        [Test]
        public async Task SendAsync_NonGrpcCall_ResponseStreamingPropertyNotSet()
        {
            // Arrange
            var request = new HttpRequestMessage
            {
                Version = GrpcWebProtocolConstants.Http2Version,
                Content = new ByteArrayContent(Array.Empty<byte>())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/text") }
                }
            };
            var testHttpHandler = new TestHttpHandler();
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, testHttpHandler);
            var messageInvoker = new HttpMessageInvoker(grpcWebHandler);

            // Act
            await messageInvoker.SendAsync(request, CancellationToken.None);

            // Assert
            Assert.AreEqual(null, testHttpHandler.WebAssemblyEnableStreamingResponse);
        }

        [Test]
        public async Task SendAsync_GrpcCallWithTrailers_TrailersSet()
        {
            // Arrange
            var data = Convert.FromBase64String("AAAAAACAAAAAEA0KZ3JwYy1zdGF0dXM6IDA=");
            var request = new HttpRequestMessage
            {
                Version = GrpcWebProtocolConstants.Http2Version,
                Content = new ByteArrayContent(Array.Empty<byte>())
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/grpc") }
                }
            };
            var testHttpHandler = new TestHttpHandler
            {
                ResponseContent = new ByteArrayContent(data)
                {
                    Headers = { ContentType = new MediaTypeHeaderValue("application/grpc-web") }
                }
            };
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, testHttpHandler);
            var messageInvoker = new HttpMessageInvoker(grpcWebHandler);

            // Act
            var response = await messageInvoker.SendAsync(request, CancellationToken.None);
            await response.Content.ReadAsByteArrayAsync();

            var trailingHeaders = response.TrailingHeaders();
            Assert.AreEqual(1, trailingHeaders.Count());
            Assert.AreEqual("0", trailingHeaders.GetValues("grpc-status").Single());
        }

        private class TestOperatingSystem : IOperatingSystem
        {
            public bool IsBrowser { get; set; }
        }

        private class TestHttpHandler : HttpMessageHandler
        {
            public HttpContent? ResponseContent { get; set; }

            public HttpRequestMessage? Request { get; private set; }
            public bool? WebAssemblyEnableStreamingResponse { get; private set; }
            public HttpRequestHeaders? RequestHeaders { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                RequestHeaders = request.Headers;
#pragma warning disable CS0618 // Type or member is obsolete
                if (request.Properties.TryGetValue(GrpcWebHandler.WebAssemblyEnableStreamingResponseKey, out var enableStreaming))
#pragma warning restore CS0618 // Type or member is obsolete
                {
                    WebAssemblyEnableStreamingResponse = (bool)enableStreaming!;
                }

                return Task.FromResult(new HttpResponseMessage()
                {
                    Version = request.Version,
                    Content = ResponseContent,
                    RequestMessage = request
                });
            }
        }
    }
}
