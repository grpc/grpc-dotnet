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
using System.IO.Pipelines;
using System.Net.Http;
using System.Threading.Tasks;
using FunctionalTestsWebsite.Infrastructure;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class DeadlineTests : FunctionalTestBase
    {
        [Test]
        public Task WriteUntilDeadline_SuccessResponsesStreamed_Deadline() =>
            WriteUntilDeadline_SuccessResponsesStreamed_CoreAsync(async (request, responseStream, context) =>
            {
                var i = 0;
                while (DateTime.UtcNow < context.Deadline)
                {
                    var message = $"How are you {request.Name}? {i}";
                    await responseStream.WriteAsync(new HelloReply { Message = message });

                    i++;

                    await Task.Delay(110);
                }

                // Ensure deadline timer has run
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                context.CancellationToken.Register(() => tcs.SetResult(null));
                await tcs.Task;
            });

        [Test]
        public Task WriteUntilDeadline_SuccessResponsesStreamed_Token() =>
            WriteUntilDeadline_SuccessResponsesStreamed_CoreAsync(async (request, responseStream, context) =>
            {
                var i = 0;
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    var message = $"How are you {request.Name}? {i}";
                    await responseStream.WriteAsync(new HelloReply { Message = message });

                    i++;

                    await Task.Delay(110);
                }
            });


        public async Task WriteUntilDeadline_SuccessResponsesStreamed_CoreAsync(ServerStreamingServerMethod<HelloRequest, HelloReply> method)
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(method);

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add(GrpcProtocolConstants.TimeoutHeader, "200m");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            var messageCount = 0;

            var readTask = Task.Run(async () =>
            {
                while (true)
                {
                    var greeting = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout();

                    if (greeting != null)
                    {
                        Assert.AreEqual($"How are you World? {messageCount}", greeting.Message);
                        messageCount++;
                    }
                    else
                    {
                        break;
                    }
                }

            });

            await readTask.DefaultTimeout();

            Assert.AreNotEqual(0, messageCount);

            response.AssertTrailerStatus(StatusCode.DeadlineExceeded, "Deadline Exceeded");
        }

        [Test]
        public async Task WriteMessageAfterDeadline()
        {
            static async Task WriteUntilError(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                var i = 0;
                while (true)
                {
                    var message = $"How are you {request.Name}? {i}";
                    await responseStream.WriteAsync(new HelloReply { Message = message });
                    i++;

                    await Task.Delay(10);
                }
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(DynamicService).FullName &&
                       writeContext.EventId.Name == "ErrorExecutingServiceMethod" &&
                       writeContext.State.ToString() == "Error when executing service method 'WriteUntilError'." &&
                       writeContext.Exception!.Message == "Cannot write message after request is complete.";
            });

            var url = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(WriteUntilError, nameof(WriteUntilError));

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequest.Headers.Add(GrpcProtocolConstants.TimeoutHeader, "200m");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            var messageCount = 0;

            var readTask = Task.Run(async () =>
            {
                while (true)
                {
                    var greeting = await MessageHelpers.AssertReadStreamMessageAsync<HelloReply>(pipeReader).DefaultTimeout();

                    if (greeting != null)
                    {
                        Assert.AreEqual($"How are you World? {messageCount}", greeting.Message);
                        messageCount++;
                    }
                    else
                    {
                        break;
                    }
                }

            });

            await readTask.DefaultTimeout();

            Assert.AreNotEqual(0, messageCount);
            response.AssertTrailerStatus(StatusCode.Unknown, "Exception was thrown by handler. InvalidOperationException: Cannot write message after request is complete.");
        }
    }
}
