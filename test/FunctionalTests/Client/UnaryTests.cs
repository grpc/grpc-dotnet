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
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class UnaryTests : FunctionalTestBase
    {
        [Test]
        public async Task ThrowErrorWithOK_ClientThrowsFailedToDeserializeError()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.State.ToString() == "Message not returned from unary or client streaming call.")
                {
                    return true;
                }

                return false;
            });

            Task<HelloReply> UnaryThrowError(HelloRequest request, ServerCallContext context)
            {
                return Task.FromException<HelloReply>(new RpcException(new Status(StatusCode.OK, "Message")));
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryThrowError);
            var channel = CreateChannel();
            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            Assert.AreEqual(StatusCode.Internal, ex.Status.StatusCode);
            Assert.AreEqual("Failed to deserialize response message.", ex.Status.Detail);

            Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
            Assert.AreEqual("Failed to deserialize response message.", call.GetStatus().Detail);
        }
    }
}
