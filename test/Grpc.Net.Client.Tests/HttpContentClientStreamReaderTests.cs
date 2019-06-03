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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class HttpContentClientStreamReaderTests
    {
        [Test]
        public void MoveNext_TokenCanceledBeforeCall_ThrowError()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var stream = new SyncPointMemoryStream();
                var content = new StreamContent(stream);
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
            });

            var call = new GrpcCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, new CallOptions(), SystemClock.Instance, NullLoggerFactory.Instance);
            call.StartServerStreaming(httpClient, new HelloRequest());

            // Act
            var moveNextTask1 = call.ClientStreamReader!.MoveNext(cts.Token);

            // Assert
            Assert.IsTrue(moveNextTask1.IsCompleted);
            var ex = Assert.ThrowsAsync<RpcException>(async () => await moveNextTask1.DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        }

        [Test]
        public void MoveNext_TokenCanceledDuringCall_ThrowError()
        {
            // Arrange
            var cts = new CancellationTokenSource();

            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var stream = new SyncPointMemoryStream();
                var content = new StreamContent(stream);
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
            });

            var call = new GrpcCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, new CallOptions(), SystemClock.Instance, NullLoggerFactory.Instance);
            call.StartServerStreaming(httpClient, new HelloRequest());

            // Act
            var moveNextTask1 = call.ClientStreamReader!.MoveNext(cts.Token);

            // Assert
            Assert.IsFalse(moveNextTask1.IsCompleted);

            cts.Cancel();

            var ex = Assert.ThrowsAsync<RpcException>(async () => await moveNextTask1.DefaultTimeout());
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        }

        [Test]
        public void MoveNext_MultipleCallsWithoutAwait_ThrowError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(request =>
            {
                var stream = new SyncPointMemoryStream();
                var content = new StreamContent(stream);
                return Task.FromResult(ResponseUtils.CreateResponse(HttpStatusCode.OK, content));
            });

            var call = new GrpcCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, new CallOptions(), SystemClock.Instance, NullLoggerFactory.Instance);
            call.StartServerStreaming(httpClient, new HelloRequest());

            // Act
            var moveNextTask1 = call.ClientStreamReader!.MoveNext(CancellationToken.None);
            var moveNextTask2 = call.ClientStreamReader.MoveNext(CancellationToken.None);

            // Assert
            Assert.IsFalse(moveNextTask1.IsCompleted);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await moveNextTask2.DefaultTimeout());
            Assert.AreEqual("Cannot read next message because the previous read is in progress.", ex.Message);
        }
    }
}
