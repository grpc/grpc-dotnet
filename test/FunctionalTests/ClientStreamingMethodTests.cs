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
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Count;
using FunctionalTestsWebsite.Services;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using NUnit.Framework;
using Grpc.Tests.Shared;

namespace Grpc.AspNetCore.FunctionalTests
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

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Count.Counter/AccumulateCount");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            var response = await responseTask.DefaultTimeout();

            var reply = await response.GetSuccessfulGrpcMessageAsync<CounterReply>();
            Assert.AreEqual(2, reply.Count);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task CompleteThenIncompleteMessage_ErrorResponse()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(CounterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'Internal' raised." &&
                       GetRpcExceptionDetail(writeContext.Exception) == "Incomplete message.";
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, "Count.Counter/AccumulateCount");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            // Complete message
            await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();

            // Incomplete message and finish
            await requestStream.AddDataAndWait(ms.ToArray().AsSpan().Slice(0, (int)ms.Length - 1).ToArray()).DefaultTimeout();
            await requestStream.AddDataAndWait(Array.Empty<byte>()).DefaultTimeout();

            var response = await responseTask.DefaultTimeout();

            // Read to end of response so headers are available
            await response.Content.CopyToAsync(new MemoryStream());

            response.AssertTrailerStatus(StatusCode.Internal, "Incomplete message.");
        }

        [Test]
        public async Task ServerMethodReturnsNull_FailureResponse()
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddClientStreamingMethod<ClientStreamingMethodTests, Empty, CounterReply>((requestStream, context) => Task.FromResult<CounterReply>(null!));

            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(ClientStreamingMethodTests).FullName &&
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
            var url = Fixture.DynamicGrpc.AddClientStreamingMethod<UnaryMethodTests, CounterRequest, CounterReply>(AccumulateCount);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new CounterRequest
            {
                Count = 1
            });

            var requestStream = new SyncPointMemoryStream();

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            _ = Task.Run(async () =>
            {
                while (!responseTask.IsCompleted)
                {
                    await requestStream.AddDataAndWait(ms.ToArray()).DefaultTimeout();
                }
            });

            var response = await responseTask.DefaultTimeout();
            var reply = await response.GetSuccessfulGrpcMessageAsync<CounterReply>().DefaultTimeout();
            Assert.AreEqual(3, reply.Count);
            response.AssertTrailerStatus();
        }
    }
}
