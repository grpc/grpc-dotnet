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
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Count;
using FunctionalTestsWebsite.Infrastructure;
using FunctionalTestsWebsite.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class ClientStreamingMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task MultipleMessagesThenClose_SuccessResponse()
        {
            // Arrange
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var httpRequest = GrpcHttpHelper.Create("Count.Counter/AccumulateCount");
            httpRequest.Content = new PushStreamContent(
                async s =>
                {
                    await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                    await s.FlushAsync().DefaultTimeout();

                    await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                    await s.FlushAsync().DefaultTimeout();

                    await tcs.Task.DefaultTimeout();
                });

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            tcs.TrySetResult(null);

            var response = await responseTask.DefaultTimeout();

            var reply = await response.GetSuccessfulGrpcMessageAsync<CounterReply>().DefaultTimeout();
            Assert.AreEqual(2, reply.Count);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task CompleteThenIncompleteMessage_ErrorResponse()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                    writeContext.EventId.Name == "ErrorReadingMessage" &&
                    writeContext.State.ToString() == "Error reading message." &&
                    GetRpcExceptionDetail(writeContext.Exception) == "Incomplete message.")
                {
                    return true;
                }

                return false;
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var httpRequest = GrpcHttpHelper.Create("Count.Counter/AccumulateCount");
            httpRequest.Content = new PushStreamContent(
                async s =>
                {
                    // Complete message
                    await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                    await s.FlushAsync().DefaultTimeout();

                    // Incomplete message and finish
                    await s.WriteAsync(ms.ToArray().AsSpan().Slice(0, (int)ms.Length - 1).ToArray()).AsTask().DefaultTimeout();
                    await s.FlushAsync().DefaultTimeout();
                });

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            var response = await responseTask.DefaultTimeout();

            // Read to end of response so headers are available
            await response.Content.CopyToAsync(new MemoryStream()).DefaultTimeout();

            response.AssertTrailerStatus(StatusCode.Internal, "Incomplete message.");

            AssertHasLogRpcConnectionError(StatusCode.Internal, "Incomplete message.");
        }

        [Test]
        public async Task ServerMethodReturnsNull_FailureResponse()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<Empty, CounterReply>((requestStream, context) => Task.FromResult<CounterReply>(null!));

            var requestMessage = new CounterRequest
            {
                Count = 1
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus(StatusCode.Cancelled, "No message returned from method.");

            AssertHasLogRpcConnectionError(StatusCode.Cancelled, "No message returned from method.");
        }

        [Test]
        public async Task ServerCancellationToken_ReturnsResponse()
        {
            static async Task<CounterReply> AccumulateCount(IAsyncStreamReader<CounterRequest> requestStream, ServerCallContext context)
            {
                var cts = new CancellationTokenSource();

                var counter = 0;
                while (true)
                {
                    try
                    {
                        var hasNext = await requestStream.MoveNext(cts.Token).DefaultTimeout();

                        if (!hasNext)
                        {
                            break;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }

                    counter += requestStream.Current.Count;

                    if (counter >= 3)
                    {
                        cts.Cancel();
                    }
                }

                return new CounterReply { Count = counter };
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<CounterRequest, CounterReply>(AccumulateCount);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var requestStream = new MemoryStream();

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Content = new PushStreamContent(
                async s =>
                {
                    for (var i = 0; i < 10; i++)
                    {
                        await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                        await s.FlushAsync().DefaultTimeout();
                    }
                });

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            var response = await responseTask.DefaultTimeout();
            var reply = await response.GetSuccessfulGrpcMessageAsync<CounterReply>().DefaultTimeout();
            Assert.AreEqual(3, reply.Count);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task StreamingEndsWithIncompleteMessage_ErrorResponse()
        {
            var counter = 0;
            async Task<CounterReply> AccumulateCount(IAsyncStreamReader<CounterRequest> requestStream, ServerCallContext context)
            {
                while (await requestStream.MoveNext().DefaultTimeout())
                {
                    counter += requestStream.Current.Count;
                }

                return new CounterReply { Count = counter };
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == TestConstants.ServerCallHandlerTestName;
            });

            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<CounterRequest, CounterReply>(AccumulateCount);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Content = new PushStreamContent(
                async s =>
                {
                    var responseData = ms.ToArray();

                    await s.WriteAsync(responseData).AsTask().DefaultTimeout();
                    await s.FlushAsync().DefaultTimeout();

                    await s.WriteAsync(responseData.AsMemory().Slice(0, responseData.Length - 1)).AsTask().DefaultTimeout();
                    await s.FlushAsync().DefaultTimeout();
                });

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            var response = await responseTask.DefaultTimeout();

            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus(StatusCode.Internal, "Incomplete message.");

            Assert.AreEqual(1, counter);
        }
    }
}
