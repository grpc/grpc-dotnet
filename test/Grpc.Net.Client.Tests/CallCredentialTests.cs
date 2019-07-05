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
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class CallCredentialTests
    {
        [Test]
        public async Task CallCredentialsWithHttps_MetadataOnRequest()
        {
            // Arrange
            string? authorizationValue = null;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                authorizationValue = request.Headers.GetValues("authorization").Single();

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var callCredentials = CallCredentials.FromInterceptor(async (context, metadata) =>
            {
                // The operation is asynchronous to ensure delegate is awaited
                await Task.Delay(50);
                metadata.Add("authorization", "SECRET_TOKEN");
            });
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(credentials: callCredentials), new HelloRequest());
            await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("SECRET_TOKEN", authorizationValue);
        }

        [Test]
        public async Task CallCredentialsWithHttp_NoMetadataOnRequest()
        {
            // Arrange
            bool? hasAuthorizationValue = null;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                hasAuthorizationValue = request.Headers.TryGetValues("authorization", out _);

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            }, new Uri("http://localhost"));

            var testSink = new TestSink();
            var loggerFactory = new TestLoggerFactory(testSink, true);

            var invoker = HttpClientCallInvokerFactory.Create(httpClient, loggerFactory);

            // Act
            var callCredentials = CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("authorization", "SECRET_TOKEN");
                return Task.CompletedTask;
            });
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(credentials: callCredentials), new HelloRequest());
            await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(false, hasAuthorizationValue);

            var log = testSink.Writes.Single(w => w.EventId.Name == "CallCredentialsNotUsed");
            Assert.AreEqual("The configured CallCredentials were not used because the call does not use TLS.", log.State.ToString());
        }

        [Test]
        public async Task CompositeCallCredentialsWithHttps_MetadataOnRequest()
        {
            // Arrange
            HttpRequestHeaders? requestHeaders = null;
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                requestHeaders = request.Headers;

                var reply = new HelloReply { Message = "Hello world" };
                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            var first = CallCredentials.FromInterceptor(new AsyncAuthInterceptor((context, metadata) => {
                metadata.Add("first_authorization", "FIRST_SECRET_TOKEN");
                return Task.CompletedTask;
            }));
            var second = CallCredentials.FromInterceptor(new AsyncAuthInterceptor((context, metadata) => {
                metadata.Add("second_authorization", "SECOND_SECRET_TOKEN");
                return Task.CompletedTask;
            }));
            var third = CallCredentials.FromInterceptor(new AsyncAuthInterceptor((context, metadata) => {
                metadata.Add("third_authorization", "THIRD_SECRET_TOKEN");
                return Task.CompletedTask;
            }));

            // Act
            var callCredentials = CallCredentials.Compose(first, second, third);
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(credentials: callCredentials), new HelloRequest());
            await call.ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("FIRST_SECRET_TOKEN", requestHeaders!.GetValues("first_authorization").Single());
            Assert.AreEqual("SECOND_SECRET_TOKEN", requestHeaders!.GetValues("second_authorization").Single());
            Assert.AreEqual("THIRD_SECRET_TOKEN", requestHeaders!.GetValues("third_authorization").Single());
        }
    }
}
