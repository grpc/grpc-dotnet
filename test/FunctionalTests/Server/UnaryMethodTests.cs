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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Any;
using FunctionalTestsWebsite.Infrastructure;
using FunctionalTestsWebsite.Services;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using AnyMessage = Google.Protobuf.WellKnownTypes.Any;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class UnaryMethodTests : FunctionalTestBase
    {
        [Test]
        public async Task SendValidRequest_SuccessResponse()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var response = await Fixture.Client.PostAsync(
                "Greet.Greeter/SayHello",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<HelloReply>().DefaultTimeout();
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task StreamedMessage_SuccessResponseAfterMessageReceived()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var streamingContent = new StreamingContent();
            var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
            httpRequest.Content = streamingContent;

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            var requestStream = await streamingContent.GetRequestStreamAsync().DefaultTimeout();
            await requestStream.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
            streamingContent.Complete();

            var response = await responseTask.DefaultTimeout();
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<HelloReply>().DefaultTimeout();

            Assert.AreEqual("Hello World", responseMessage.Message);
        }

        [Test]
        public async Task AdditionalDataAfterStreamedMessage_ErrorResponse()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                    writeContext.EventId.Name == "ErrorReadingMessage" &&
                    writeContext.State.ToString() == "Error reading message.")
                {
                    return true;
                }

                return false;
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var streamingContent = new StreamingContent();
            var httpRequest = GrpcHttpHelper.Create("Greet.Greeter/SayHello");
            httpRequest.Content = streamingContent;

            // Act
            var responseTask = Fixture.Client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead);

            // Assert
            Assert.IsFalse(responseTask.IsCompleted, "Server should wait for client to finish streaming");

            var requestStream = await streamingContent.GetRequestStreamAsync().DefaultTimeout();
            await requestStream.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
            await requestStream.WriteAsync(ms.ToArray()).AsTask().DefaultTimeout();
            streamingContent.Complete();

            var response = await responseTask.DefaultTimeout();

            // Read to end of response so headers are available
            await response.Content.CopyToAsync(new MemoryStream()).DefaultTimeout();

            response.AssertTrailerStatus(StatusCode.Internal, "Additional data after the message received.");

            AssertHasLogRpcConnectionError(StatusCode.Internal, "Additional data after the message received.");
        }

        [Test]
        public async Task SendHeadersTwice_ThrowsException()
        {
            static async Task<HelloReply> ReturnHeadersTwice(HelloRequest request, ServerCallContext context)
            {
                await context.WriteResponseHeadersAsync(null).DefaultTimeout();

                await context.WriteResponseHeadersAsync(null).DefaultTimeout();

                return new HelloReply { Message = "Should never reach here" };
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                       writeContext.EventId.Name == "ErrorExecutingServiceMethod" &&
                       writeContext.State.ToString() == "Error when executing service method 'ReturnHeadersTwice'." &&
                       writeContext.Exception!.Message == "Response headers can only be sent once per call.";
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(ReturnHeadersTwice, nameof(ReturnHeadersTwice));

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            response.AssertTrailerStatus(StatusCode.Unknown, "Exception was thrown by handler. InvalidOperationException: Response headers can only be sent once per call.");
        }

        [Test]
        public async Task ServerMethodReturnsNull_FailureResponse()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(
                (requestStream, context) => Task.FromResult<HelloReply>(null!));

            var requestMessage = new HelloRequest
            {
                Name = "World"
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
        public async Task ServerMethodThrowsExceptionWithTrailers_FailureResponse()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>((request, context) =>
            {
                var trailers = new Metadata
                {
                    new Metadata.Entry("test-trailer", "A value!")
                };

                return Task.FromException<HelloReply>(new RpcException(new Status(StatusCode.Unknown, "User error"), trailers));
            });

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            response.AssertTrailerStatus(StatusCode.Unknown, "User error");
            // Trailer is written to the header because this is a Trailers-Only response
            Assert.AreEqual("A value!", response.Headers.GetValues("test-trailer").Single());

            AssertHasLogRpcConnectionError(StatusCode.Unknown, "User error");
        }

        [Test]
        public async Task ServerMethodThrowsNonRpcException_FailureResponse()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                       writeContext.EventId.Name == "ErrorExecutingServiceMethod" &&
                       writeContext.State.ToString() == "Error when executing service method 'GrpcErrorMethod'." &&
                       writeContext.Exception!.Message == "Non RPC exception.";
            });

            var ex = new InvalidOperationException("Non RPC exception.");
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>((request, context) =>
            {
                return Task.FromException<HelloReply>(ex);
            }, "GrpcErrorMethod");

            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            response.AssertIsSuccessfulGrpcRequest();

            response.AssertTrailerStatus(StatusCode.Unknown, "Exception was thrown by handler. InvalidOperationException: Non RPC exception.");

            AssertHasLog(LogLevel.Error, "ErrorExecutingServiceMethod", "Error when executing service method 'GrpcErrorMethod'.", e => e == ex);
        }

        [Test]
        public async Task ValidRequest_ReturnContextInfoInTrailers()
        {
            static Task<Empty> ReturnContextInfoInTrailers(Empty request, ServerCallContext context)
            {
                context.ResponseTrailers.Add("Test-Method", context.Method);
                context.ResponseTrailers.Add("Test-Peer", context.Peer);
                context.ResponseTrailers.Add("Test-Host", context.Host);

                return Task.FromResult(new Empty());
            }

            // Arrange
            var requestMessage = new Empty();

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var method = Fixture.DynamicGrpc.AddUnaryMethod<Empty, Empty>(ReturnContextInfoInTrailers);

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<Empty>().DefaultTimeout();
            Assert.IsNotNull(responseMessage);

            var methodParts = response.TrailingHeaders.GetValues("Test-Method").Single().Split('/', StringSplitOptions.RemoveEmptyEntries);
            var serviceName = methodParts[0];
            var methodName = methodParts[1];

            Assert.AreEqual("DynamicService", serviceName);
            Assert.IsTrue(Guid.TryParse(methodName, out var _));

            Assert.IsTrue(response.TrailingHeaders.TryGetValues("Test-Peer", out _));
            Assert.AreEqual(Fixture.Client.BaseAddress!.Authority, response.TrailingHeaders.GetValues("Test-Host").Single());
        }

        [Test]
        public async Task ThrowErrorInNonAsyncMethod_StatusMessageReturned()
        {
            static Task<Empty> ReturnContextInfoInTrailers(Empty request, ServerCallContext context)
            {
                throw new Exception("Test!");
            }

            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                       writeContext.EventId.Name == "ErrorExecutingServiceMethod";
            });

            // Arrange
            var requestMessage = new Empty();

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            var method = Fixture.DynamicGrpc.AddUnaryMethod<Empty, Empty>(ReturnContextInfoInTrailers);

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            response.AssertTrailerStatus(StatusCode.Unknown, "Exception was thrown by handler. Exception: Test!");
        }

        [Test]
        public async Task SingletonService_PrivateFieldsPreservedBetweenCalls()
        {
            // Arrange 1
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act 1
            var response = await Fixture.Client.PostAsync(
                "singleton_count.Counter/IncrementCount",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert 1
            var total = await response.GetSuccessfulGrpcMessageAsync<SingletonCount.CounterReply>().DefaultTimeout();
            Assert.AreEqual(1, total.Count);
            response.AssertTrailerStatus();

            // Arrange 2
            ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act 2
            response = await Fixture.Client.PostAsync(
                "singleton_count.Counter/IncrementCount",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert 2
            total = await response.GetSuccessfulGrpcMessageAsync<SingletonCount.CounterReply>().DefaultTimeout();
            Assert.AreEqual(2, total.Count);
            response.AssertTrailerStatus();
        }

        [TestCase(null, "Content-Type is missing from the request.")]
        [TestCase("application/json", "Content-Type 'application/json' is not supported.")]
        [TestCase("application/binary", "Content-Type 'application/binary' is not supported.")]
        public async Task InvalidContentType_Return415Response(string contentType, string responseMessage)
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);
            var streamContent = new StreamContent(ms);
            streamContent.Headers.ContentType = contentType != null ? new MediaTypeHeaderValue(contentType) : null;

            // Act
            var response = await Fixture.Client.PostAsync(
                "Greet.Greeter/SayHello",
                streamContent).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);

            response.AssertTrailerStatus(StatusCode.Internal, responseMessage);
        }

        [Test]
        public async Task InvalidProtocol_Return426Response()
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);
            var streamContent = new StreamContent(ms);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

            var client = Fixture.CreateClient(TestServerEndpointName.Http1);

            // Act
            var response = await client.PostAsync(
                "Greet.Greeter/SayHello",
                streamContent).DefaultTimeout();

            // Assert
            Assert.AreEqual(HttpStatusCode.UpgradeRequired, response.StatusCode);

            var upgradeValue = response.Headers.Upgrade.Single();
            Assert.AreEqual("HTTP", upgradeValue.Name);
            Assert.AreEqual("2", upgradeValue.Version);

            response.AssertTrailerStatus(StatusCode.Internal, "Request protocol 'HTTP/1.1' is not supported.");
        }

        [TestCase("application/grpc")]
        [TestCase("APPLICATION/GRPC")]
        [TestCase("application/grpc+proto")]
        [TestCase("APPLICATION/GRPC+PROTO")]
        [TestCase("application/grpc+json")] // Accept any message format. A Method+marshaller may have been set that reads and writes JSON
        [TestCase("application/grpc; param=one")]
        public async Task ValidContentType_ReturnValidResponse(string contentType)
        {
            // Arrange
            var requestMessage = new HelloRequest
            {
                Name = "World"
            };

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);
            var streamContent = new StreamContent(ms);
            streamContent.Headers.ContentType = contentType != null ? MediaTypeHeaderValue.Parse(contentType) : null;

            // Act
            var response = await Fixture.Client.PostAsync(
                "Greet.Greeter/SayHello",
                streamContent).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<HelloReply>().DefaultTimeout();
            Assert.AreEqual("Hello World", responseMessage.Message);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task AnyRequest_SuccessResponse()
        {
            // Arrange 1
            IMessage requestMessage = AnyMessage.Pack(new AnyProductRequest
            {
                Name = "Headlight fluid",
                Quantity = 2
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act 1
            var response = await Fixture.Client.PostAsync(
                "any.AnyService/DoAny",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert 1
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<AnyMessageResponse>().DefaultTimeout();
            Assert.AreEqual("2 x Headlight fluid", responseMessage.Message);
            response.AssertTrailerStatus();

            // Arrange 2
            requestMessage = AnyMessage.Pack(new AnyUserRequest
            {
                Name = "Arnie Admin",
                Enabled = true
            });

            ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, requestMessage);

            // Act 2
            response = await Fixture.Client.PostAsync(
                "any.AnyService/DoAny",
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert 2
            responseMessage = await response.GetSuccessfulGrpcMessageAsync<AnyMessageResponse>().DefaultTimeout();
            Assert.AreEqual("Arnie Admin - Enabled", responseMessage.Message);
            response.AssertTrailerStatus();
        }
    }
}
