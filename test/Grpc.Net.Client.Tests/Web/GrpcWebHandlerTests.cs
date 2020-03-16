﻿#region Copyright notice and license

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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client.Web;
using Grpc.Net.Client.Web.Internal;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Web
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
                Version = HttpVersion.Version20,
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
            Assert.AreEqual(HttpVersion.Version20, testHttpHandler.RequestVersion);
            Assert.AreEqual(HttpVersion.Version20, response.Version);
        }

        [Test]
        public async Task HttpVersion_Set_HttpRequestMessageVersionChanged()
        {
            // Arrange
            var request = new HttpRequestMessage
            {
                Version = HttpVersion.Version20,
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
            Assert.AreEqual(HttpVersion.Version11, testHttpHandler.RequestVersion);
            Assert.AreEqual(HttpVersion.Version20, response.Version);
        }

        private class TestHttpHandler : HttpMessageHandler
        {
            public Version? RequestVersion { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestVersion = request.Version;

                return Task.FromResult(new HttpResponseMessage()
                {
                    Version = request.Version
                });
            }
        }
    }
}
