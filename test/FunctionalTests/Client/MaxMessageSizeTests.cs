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

using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class MaxMessageSizeTests : FunctionalTestBase
    {
        [Test]
        public async Task ReceivedMessageExceedsDefaultSize_ThrowError()
        {
            Task<HelloReply> UnaryDeadlineExceeded(HelloRequest request, ServerCallContext context)
            {
                // Return message is 4 MB + 1 B. Default receive size is 4 MB
                return Task.FromResult(new HelloReply { Message = new string('!', (1024 * 1024 * 4) + 1) });
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorReadingMessage" &&
                    writeContext.State.ToString() == "Error reading message.")
                {
                    return true;
                }

                return false;
            });

            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryDeadlineExceeded);

            var client = TestClientFactory.Create(Fixture.Client, LoggerFactory, method, disableClientDeadlineTimer: true);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.UnaryCall(new HelloRequest()).ResponseAsync).DefaultTimeout();

            // Assert
            Assert.AreEqual(StatusCode.ResourceExhausted, ex.StatusCode);
            Assert.AreEqual("Received message exceeds the maximum configured message size.", ex.Status.Detail);
        }
    }
}
