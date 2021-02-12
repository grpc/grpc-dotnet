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
using System.Threading.Tasks;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;
using Streaming;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class DeadlineTests : FunctionalTestBase
    {
        [Test]
        public async Task Unary_SmallDeadline_ExceededWithoutReschedule()
        {
            var tcs = new TaskCompletionSource<DataMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<DataMessage> UnaryTimeout(DataMessage request, ServerCallContext context)
            {
                return tcs.Task;
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryTimeout);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new DataMessage(), new CallOptions(deadline: DateTime.UtcNow.AddMilliseconds(200)));

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
            Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);

            Assert.IsFalse(Logs.Any(l => l.EventId.Name == "DeadlineTimerRescheduled"));

            tcs.SetResult(new DataMessage());
        }
    }
}
