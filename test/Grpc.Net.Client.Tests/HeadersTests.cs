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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Net.Http.Headers;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class HeadersTests
    {
        [Test]
        public async Task AsyncUnaryCall_SendHeaders_RequestMessageContainsHeaders()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await TestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            var headers = new Metadata();
            headers.Add("custom", "ascii");
            headers.Add("custom-bin", Encoding.UTF8.GetBytes("Hello world"));

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(headers: headers), new HelloRequest());

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual("ascii", httpRequestMessage!.Headers.GetValues("custom").Single());
            Assert.AreEqual("Hello world", Encoding.UTF8.GetString(Convert.FromBase64String(httpRequestMessage.Headers.GetValues("custom-bin").Single())));
        }

        [Test]
        public async Task AsyncUnaryCall_NoHeaders_RequestMessageHasNoHeaders()
        {
            // Arrange
            HttpRequestMessage? httpRequestMessage = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                HelloReply reply = new HelloReply
                {
                    Message = "Hello world"
                };

                var streamContent = await TestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            var headers = new Metadata();

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(headers: headers), new HelloRequest());

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(httpRequestMessage);

            // User-Agent is always sent
            Assert.AreEqual(0, httpRequestMessage!.Headers.Count(h =>
            {
                if (string.Equals(h.Key, HeaderNames.UserAgent, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (string.Equals(h.Key, HeaderNames.TE, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }));
        }
    }
}
