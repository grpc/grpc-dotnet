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
using System.Threading.Tasks;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http.Features;
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

        [Test]
        public async Task Unary_ServerResetCancellationStatus_DeadlineStatus()
        {
            TaskCompletionSource<object?> tcs = null!;
            async Task<DataMessage> UnaryTimeout(DataMessage request, ServerCallContext context)
            {
                var httpContext = context.GetHttpContext();
                var resetFeature = httpContext.Features.Get<IHttpResetFeature>()!;

                await tcs.Task;

                // Reset needs to arrive in client after it has exceeded deadline.
                // Delay can be imprecise. Wait extra time to ensure client has exceeded deadline.
                await Task.Delay(50);

                var cancelErrorCode = (httpContext.Request.Protocol == "HTTP/2") ? 0x8 : 0x10c;
                resetFeature.Reset(cancelErrorCode);

                return new DataMessage();
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<DataMessage, DataMessage>(UnaryTimeout);

            var channel = CreateChannel();
            channel.DisableClientDeadline = true;

            var client = TestClientFactory.Create(channel, method);
            var deadline = TimeSpan.FromMilliseconds(300);

            for (var i = 0; i < 5; i++)
            {
                tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Act
                var headers = new Metadata
                {
                    { "remove-deadline", "true" }
                };
                var call = client.UnaryCall(new DataMessage(), new CallOptions(headers: headers, deadline: DateTime.UtcNow.Add(deadline)));

                await Task.Delay(deadline);
                tcs.SetResult(null);

                // Assert
                var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
                Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
                Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            }
        }

        [Test]
        public async Task AsyncUnaryCall_ExceedDeadlineWithActiveCalls_Failure()//(int i)
        {
            TaskCompletionSource<object?> tcs = null!;
            async Task ServerStreamingTimeout(DataMessage request, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
            {
                var httpContext = context.GetHttpContext();
                var resetFeature = httpContext.Features.Get<IHttpResetFeature>()!;

                await tcs.Task;

                // Reset needs to arrive in client after it has exceeded deadline.
                // Delay can be imprecise. Wait extra time to ensure client has exceeded deadline.
                await Task.Delay(50);

                var cancelErrorCode = (httpContext.Request.Protocol == "HTTP/2") ? 0x8 : 0x10c;
                resetFeature.Reset(cancelErrorCode);
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<DataMessage, DataMessage>(ServerStreamingTimeout);

            var channel = CreateChannel();
            channel.DisableClientDeadline = true;

            var client = TestClientFactory.Create(channel, method);
            var deadline = TimeSpan.FromMilliseconds(300);

            for (var i = 0; i < 5; i++)
            {
                tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

                // Act
                var headers = new Metadata
                {
                    { "remove-deadline", "true" }
                };
                var call = client.ServerStreamingCall(new DataMessage(), new CallOptions(headers: headers, deadline: DateTime.UtcNow.Add(deadline)));

                await Task.Delay(deadline);
                tcs.SetResult(null);

                // Assert
                var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();
                Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
                Assert.AreEqual(StatusCode.DeadlineExceeded, call.GetStatus().StatusCode);
            }
        }
    }
}
