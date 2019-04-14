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
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.NetCore.HttpClient.Internal;
using Grpc.NetCore.HttpClient.Tests.Infrastructure;
using Grpc.Tests;
using NUnit.Framework;

namespace Grpc.NetCore.HttpClient.Tests
{
    [TestFixture]
    public class DeadlineTests
    {
        [Test]
        public async Task AsyncUnaryCall_SetSecondDeadline_RequestMessageContainsDeadlineHeader()
        {
            // Arrange
            HttpRequestMessage httpRequestMessage = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = new HttpClientCallInvoker(httpClient);
            invoker.Clock = new TestSystemClock(new DateTime(2019, 11, 29, 1, 1, 1, DateTimeKind.Utc));

            // Act
            await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, null, new CallOptions(deadline: invoker.Clock.UtcNow.AddSeconds(1)), new HelloRequest());

            // Assert
            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual(1, httpRequestMessage.Headers.Count());
            Assert.AreEqual("1000m", httpRequestMessage.Headers.GetValues("grpc-timeout").Single());
        }

        [Test]
        public async Task AsyncUnaryCall_SetMaxValueDeadline_RequestMessageHasNoDeadlineHeader()
        {
            // Arrange
            HttpRequestMessage httpRequestMessage = null;

            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                httpRequestMessage = request;

                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = new HttpClientCallInvoker(httpClient);

            // Act
            await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, null, new CallOptions(deadline: DateTime.MaxValue), new HelloRequest());

            // Assert
            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual(0, httpRequestMessage.Headers.Count());
        }

        [Test]
        public async Task AsyncUnaryCall_SendDeadlineHeaderAndDeadlineValue_DeadlineValueIsUsed()
        {
            // Arrange
            HttpRequestMessage httpRequestMessage = null;

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
            var invoker = new HttpClientCallInvoker(httpClient);
            invoker.Clock = new TestSystemClock(new DateTime(2019, 11, 29, 1, 1, 1, DateTimeKind.Utc));

            var headers = new Metadata();
            headers.Add("grpc-timeout", "1D");

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, null, new CallOptions(headers: headers, deadline: invoker.Clock.UtcNow.AddSeconds(1)), new HelloRequest());

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            Assert.IsNotNull(httpRequestMessage);
            Assert.AreEqual(1, httpRequestMessage.Headers.Count());
            Assert.AreEqual("1000m", httpRequestMessage.Headers.GetValues("grpc-timeout").Single());
        }

        private class TestSystemClock : ISystemClock
        {
            public TestSystemClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; }
        }
    }
}
