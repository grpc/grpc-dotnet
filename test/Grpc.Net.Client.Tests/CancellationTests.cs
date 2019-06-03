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

using System.Net;
using System.Threading;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class CancellationTests
    {
        [Test]
        public void AsyncClientStreamingCall_CancellationDuringSend_ResponseThrowsCancelledStatus()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var invoker = CreateTimedoutCallInvoker();

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(cancellationToken: cts.Token));

            // Assert
            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

            cts.Cancel();

            var ex = Assert.ThrowsAsync<RpcException>(async () => await responseTask.DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.Status.StatusCode);
        }

        [Test]
        public void AsyncClientStreamingCall_CancellationDuringSend_ResponseHeadersThrowsCancelledStatus()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var invoker = CreateTimedoutCallInvoker();

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(cancellationToken: cts.Token));

            // Assert
            var responseHeadersTask = call.ResponseHeadersAsync;
            Assert.IsFalse(responseHeadersTask.IsCompleted, "Headers not returned until client stream is complete.");

            cts.Cancel();

            var ex = Assert.ThrowsAsync<RpcException>(async () => await responseHeadersTask.DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.Status.StatusCode);
        }

        [Test]
        public void AsyncClientStreamingCall_CancellationDuringSend_TrailersThrowsCancelledStatus()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var invoker = CreateTimedoutCallInvoker();

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(cancellationToken: cts.Token));

            // Assert
            cts.Cancel();

            var ex = Assert.Throws<RpcException>(() => call.GetTrailers());

            Assert.AreEqual(StatusCode.Cancelled, ex.Status.StatusCode);
        }

        [Test]
        public void AsyncClientStreamingCall_CancellationTokenOnCallInvoker_ResponseThrowsCancelledStatus()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var invoker = CreateTimedoutCallInvoker();
            invoker.CancellationToken = cts.Token;

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions());

            // Assert
            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

            cts.Cancel();

            var ex = Assert.ThrowsAsync<RpcException>(async () => await responseTask.DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.Status.StatusCode);
        }

        [Test]
        public void AsyncClientStreamingCall_CancellationTokenOnCallInvokerAndOptions_ResponseThrowsCancelledStatus()
        {
            // Arrange
            var invokerCts = new CancellationTokenSource();
            var optionsCts = new CancellationTokenSource();

            var invoker = CreateTimedoutCallInvoker();
            invoker.CancellationToken = invokerCts.Token;

            // Act
            var call = invoker.AsyncClientStreamingCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(cancellationToken: optionsCts.Token));

            // Assert
            var responseTask = call.ResponseAsync;
            Assert.IsFalse(responseTask.IsCompleted, "Response not returned until client stream is complete.");

            invokerCts.Cancel();

            var ex = Assert.ThrowsAsync<RpcException>(async () => await responseTask.DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.Status.StatusCode);

            call.Dispose();
        }

        private static HttpClientCallInvoker CreateTimedoutCallInvoker()
        {
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var content = (PushStreamContent)request.Content;
                await content.PushComplete.DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK);
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);
            return invoker;
        }
    }
}
