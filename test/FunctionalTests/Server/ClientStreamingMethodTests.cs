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
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Count;
using FunctionalTestsWebsite.Infrastructure;
using FunctionalTestsWebsite.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
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
                });

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");


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
                return writeContext.LoggerName == "SERVER " + typeof(CounterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'Internal' raised." &&
                       GetRpcExceptionDetail(writeContext.Exception) == "Incomplete message.";
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
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            var response = await responseTask.DefaultTimeout();

            // Read to end of response so headers are available
            await response.Content.CopyToAsync(new MemoryStream()).DefaultTimeout();

            response.AssertTrailerStatus(StatusCode.Internal, "Incomplete message.");
        }

        [Test]
        public async Task ServerMethodReturnsNull_FailureResponse()
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddClientStreamingMethod<Empty, CounterReply>((requestStream, context) => Task.FromResult<CounterReply>(null!));

            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == "SERVER " + typeof(DynamicService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'Cancelled' raised." &&
                       GetRpcExceptionDetail(writeContext.Exception) == "No message returned from method.";
            });

            var requestMessage = new CounterRequest
            {
                Count = 1
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus(StatusCode.Cancelled, "No message returned from method.");
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
            var url = Fixture.DynamicGrpc.AddClientStreamingMethod<CounterRequest, CounterReply>(AccumulateCount);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var requestStream = new MemoryStream();

            var httpRequest = GrpcHttpHelper.Create(url);
            httpRequest.Content = new PushStreamContent(
                async s =>
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await s.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
                    }
                });

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            var response = await responseTask.DefaultTimeout();
            var reply = await response.GetSuccessfulGrpcMessageAsync<CounterReply>().DefaultTimeout();
            Assert.AreEqual(3, reply.Count);
            response.AssertTrailerStatus();
        }
    }
}
