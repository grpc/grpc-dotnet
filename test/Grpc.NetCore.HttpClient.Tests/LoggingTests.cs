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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.NetCore.HttpClient.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.NetCore.HttpClient.Tests
{
    [TestFixture]
    public class LoggingTests
    {
        [Test]
        public async Task AsyncUnaryCall_Success_LogToFactory()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                // Trigger request stream serialization
                await request.Content.ReadAsStreamAsync().DefaultTimeout();

                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply
                {
                    Message = "Hello world"
                }).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var testSink = new TestSink();
            var loggerFactory = new TestLoggerFactory(testSink, true);

            var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

            // Act
            var rs = await invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            Assert.AreEqual("Hello world", rs.Message);

            var logs = testSink.Writes.Where(w => w.LogLevel >= Microsoft.Extensions.Logging.LogLevel.Debug).ToList();

            Assert.AreEqual("Starting gRPC call. Method type: 'Unary', URI: '/ServiceName/MethodName'.", logs[0].State.ToString());
            AssertScope(logs[0]);

            Assert.AreEqual("Sending message.", logs[1].State.ToString());
            AssertScope(logs[1]);

            Assert.AreEqual("Reading message.", logs[2].State.ToString());
            AssertScope(logs[2]);

            Assert.AreEqual("Finished gRPC call.", logs[3].State.ToString());
            AssertScope(logs[3]);

            static void AssertScope(WriteContext log)
            {
                var scope = (IReadOnlyList<KeyValuePair<string, object>>)log.Scope;
                Assert.AreEqual(MethodType.Unary, scope[0].Value);
                Assert.AreEqual(new Uri("/ServiceName/MethodName", UriKind.Relative), scope[1].Value);
            }
        }
    }
}
