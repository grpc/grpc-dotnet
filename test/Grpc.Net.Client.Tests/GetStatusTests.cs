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
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests
{
    [TestFixture]
    public class GetStatusTests
    {
        [Test]
        public void AsyncUnaryCall_ValidStatusReturned_ReturnsStatus()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync.DefaultTimeout());
            Assert.AreEqual(StatusCode.Aborted, ex.StatusCode);

            var status = call.GetStatus();
            Assert.AreEqual(StatusCode.Aborted, status.StatusCode);
            Assert.AreEqual("value", status.Detail);
        }

        [Test]
        public void AsyncUnaryCall_PercentEncodedMessage_MessageDecoded()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "%C2%A3");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = Assert.ThrowsAsync<RpcException>(async () => await call.ResponseAsync.DefaultTimeout());
            Assert.AreEqual(StatusCode.Aborted, ex.StatusCode);

            var status = call.GetStatus();
            Assert.AreEqual(StatusCode.Aborted, status.StatusCode);
            Assert.AreEqual("£", status.Detail);
        }

        [Test]
        public void AsyncUnaryCall_MultipleStatusHeaders_ThrowError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "one");
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "two");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseAsync.DefaultTimeout());
            Assert.AreEqual("Multiple grpc-message headers.", ex.Message);
        }

        [Test]
        public void AsyncUnaryCall_MissingStatus_ThrowError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: null);
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseAsync.DefaultTimeout());

            var ex = Assert.Throws<InvalidOperationException>(() => call.GetStatus());
            Assert.AreEqual("Response did not have a grpc-status trailer.", ex.Message);
        }

        [Test]
        public void AsyncUnaryCall_InvalidStatus_ThrowError()
        {
            // Arrange
            var httpClient = TestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await TestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: null);
                response.TrailingHeaders.Add(GrpcProtocolConstants.StatusTrailer, "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(TestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            Assert.ThrowsAsync<InvalidOperationException>(async () => await call.ResponseAsync.DefaultTimeout());

            var ex = Assert.Throws<InvalidOperationException>(() => call.GetStatus());
            Assert.AreEqual("Unexpected grpc-status value: value", ex.Message);
        }
    }
}
