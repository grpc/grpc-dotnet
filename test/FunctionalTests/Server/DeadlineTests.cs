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
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using FunctionalTestsWebsite.Infrastructure;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
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
                    await responseStream.WriteAsync(new HelloReply { Message = message }).DefaultTimeout();

                    i++;

                    await Task.Delay(110);
                }

                // Ensure deadline timer has run
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                context.CancellationToken.Register(() => tcs.SetResult(null));
                await tcs.Task.DefaultTimeout();
            });

        [Test]
        public Task WriteUntilDeadline_SuccessResponsesStreamed_Token() =>
            WriteUntilDeadline_SuccessResponsesStreamed_CoreAsync(async (request, responseStream, context) =>
            {
                var i = 0;
                while (!context.CancellationToken.IsCancellationRequested)
                {
                    var message = $"How are you {request.Name}? {i}";
                    await responseStream.WriteAsync(new HelloReply { Message = message }).DefaultTimeout();

                    i++;

                    await Task.Delay(110);
                }
            });


        public async Task WriteUntilDeadline_SuccessResponsesStreamed_CoreAsync(ServerStreamingServerMethod<HelloRequest, HelloReply> callHandler)
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(callHandler);

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
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

            Assert.True(HasLog(LogLevel.Debug, "DeadlineExceeded", "Request with timeout of 00:00:00.2000000 has exceeded its deadline."));

            await TestHelpers.AssertIsTrueRetryAsync(
                () => HasLog(LogLevel.Trace, "DeadlineStopped", "Request deadline stopped."),
                "Missing deadline stopped log.").DefaultTimeout();
        }

        [Test]
        public async Task UnaryMethodErrorWithinDeadline()
        {
            static async Task<HelloReply> ThrowErrorWithinDeadline(HelloRequest request, ServerCallContext context)
            {
                await Task.Delay(10);
                throw new InvalidOperationException("An error.");
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName)
                {
                    // Deadline happened before write
                    if (writeContext.EventId.Name == "ErrorExecutingServiceMethod" &&
                        writeContext.State.ToString() == "Error when executing service method 'ThrowErrorWithinDeadline'." &&
                        writeContext.Exception!.Message == "An error.")
                    {
                        return true;
                    }
                }

                return false;
            });

            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(ThrowErrorWithinDeadline, nameof(ThrowErrorWithinDeadline));

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Headers.Add(GrpcProtocolConstants.TimeoutHeader, "200m");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus(StatusCode.Unknown, "Exception was thrown by handler. InvalidOperationException: An error.");

            Assert.True(HasLog(LogLevel.Trace, "DeadlineStopped", "Request deadline stopped."));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task UnaryMethodDeadlineExceeded(bool throwErrorOnCancellation)
        {
            async Task<HelloReply> WaitUntilDeadline(HelloRequest request, ServerCallContext context)
            {
                try
                {
                    await Task.Delay(1000, context.CancellationToken);
                }
                catch (OperationCanceledException) when (!throwErrorOnCancellation)
                {
                    // nom nom nom
                }
                
                return new HelloReply();
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName)
                {
                    if (writeContext.EventId.Name == "ErrorExecutingServiceMethod" &&
                        writeContext.State.ToString() == "Error when executing service method 'WaitUntilDeadline-True'.")
                    {
                        return true;
                    }
                }

                return false;
            });

            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(WaitUntilDeadline, $"{nameof(WaitUntilDeadline)}-{throwErrorOnCancellation}");

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
            httpRequest.Headers.Add(GrpcProtocolConstants.TimeoutHeader, "200m");
            httpRequest.Content = new GrpcStreamContent(requestStream);

            // Act
            var response = await Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();
            response.AssertTrailerStatus(StatusCode.DeadlineExceeded, "Deadline Exceeded");

            Assert.True(HasLog(LogLevel.Debug, "DeadlineExceeded", "Request with timeout of 00:00:00.2000000 has exceeded its deadline."));
            
            await TestHelpers.AssertIsTrueRetryAsync(
                () => HasLog(LogLevel.Trace, "DeadlineStopped", "Request deadline stopped."),
                "Missing deadline stopped log.").DefaultTimeout();
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
                    await responseStream.WriteAsync(new HelloReply { Message = message }).DefaultTimeout();
                    i++;

                    await Task.Delay(10);
                }
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName)
                {
                    // Deadline happened before write
                    if (writeContext.EventId.Name == "ErrorExecutingServiceMethod" &&
                        writeContext.State.ToString() == "Error when executing service method 'WriteUntilError'.")
                    {
                        return true;
                    }

                    // Deadline happened during write (error raised from pipeline writer)
                    if (writeContext.Exception is InvalidOperationException &&
                        writeContext.Exception.Message == "Writing is not allowed after writer was completed.")
                    {
                        return true;
                    }
                }

                return false;
            });

            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(WriteUntilError, nameof(WriteUntilError));

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var requestStream = new MemoryStream();
            MessageHelpers.WriteMessage(requestStream, requestMessage);

            var httpRequest = GrpcHttpHelper.Create(method.FullName);
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

            Assert.True(HasLog(LogLevel.Debug, "DeadlineExceeded", "Request with timeout of 00:00:00.2000000 has exceeded its deadline."));

            // The server has completed the response but is still running
            // Allow time for the server to complete
            await TestHelpers.AssertIsTrueRetryAsync(() =>
            {
                var errorLogged = Logs.Any(r =>
                    r.EventId.Name == "ErrorExecutingServiceMethod" &&
                    r.State.ToString() == "Error when executing service method 'WriteUntilError'." &&
                    (r.Exception!.Message == "Can't write the message because the request is complete." || r.Exception!.Message == "Writing is not allowed after writer was completed."));

                return errorLogged;
            }, "Expected error not thrown.").DefaultTimeout();

            await TestHelpers.AssertIsTrueRetryAsync(
                () => HasLog(LogLevel.Trace, "DeadlineStopped", "Request deadline stopped."),
                "Missing deadline stopped log.").DefaultTimeout();
        }
    }
}
