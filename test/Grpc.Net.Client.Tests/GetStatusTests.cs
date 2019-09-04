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
using System.Threading.Tasks;
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
        public async Task AsyncUnaryCall_ValidStatusReturned_ReturnsStatus()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Aborted, ex.StatusCode);

            var status = call.GetStatus();
            Assert.AreEqual(StatusCode.Aborted, status.StatusCode);
            Assert.AreEqual("value", status.Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_PercentEncodedMessage_MessageDecoded()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "%C2%A3");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Aborted, ex.StatusCode);

            var status = call.GetStatus();
            Assert.AreEqual(StatusCode.Aborted, status.StatusCode);
            Assert.AreEqual("£", status.Detail);
        }

        [Test]
        public async Task AsyncUnaryCall_MultipleStatusHeaders_ThrowError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: StatusCode.Aborted);
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "one");
                response.TrailingHeaders.Add(GrpcProtocolConstants.MessageTrailer, "two");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            Assert.AreEqual("Multiple grpc-message headers.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_MissingStatus_ThrowError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: null);
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            Assert.AreEqual("No grpc-status found on response.", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_InvalidStatus_ThrowError()
        {
            // Arrange
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent, grpcStatusCode: null);
                response.TrailingHeaders.Add(GrpcProtocolConstants.StatusTrailer, "value");
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            Assert.AreEqual("Unexpected grpc-status value: value", ex.Status.Detail);
            Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
            Assert.AreEqual(StatusCode.Cancelled, call.GetStatus().StatusCode);
        }

        [Test]
        public async Task AsyncUnaryCall_CallInProgress_ThrowError()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var httpClient = ClientTestHelpers.CreateTestClient(async request =>
            {
                await tcs.Task.DefaultTimeout();
                var streamContent = await ClientTestHelpers.CreateResponseContent(new HelloReply()).DefaultTimeout();
                var response = ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
                return response;
            });
            var invoker = HttpClientCallInvokerFactory.Create(httpClient);

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions(), new HelloRequest());
            var ex = Assert.Throws<InvalidOperationException>(() => call.GetStatus());

            // Assert
            Assert.AreEqual("Unable to get the status because the call is not complete.", ex.Message);

            tcs.TrySetResult(null);

            await call.ResponseAsync.DefaultTimeout();

            Assert.AreEqual(StatusCode.OK, call.GetStatus().StatusCode);
        }
    }
}
