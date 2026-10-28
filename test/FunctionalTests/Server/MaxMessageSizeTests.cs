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

using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server;

[TestFixture]
public class MaxMessageSizeTests : FunctionalTestBase
{
    [TestCase("gzip")]
    [TestCase("deflate")]
    public async Task ReceivedCompressedMessageExceedsSize_ThrowError(string encoding)
    {
        SetExpectedErrorsFilter(writeContext =>
            writeContext.EventId.Name == "ErrorReadingMessage" &&
            GetRpcExceptionDetail(writeContext.Exception) == "Received message exceeds the maximum configured message size.");

        var invoked = false;
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>((request, context) =>
        {
            invoked = true;
            return Task.FromResult(new HelloReply());
        });
        using var ms = new MemoryStream();
        MessageHelpers.WriteMessage(ms, new HelloRequest { Name = new string('!', 4 * 1024 * 1024 + 1) }, encoding);
        Assert.Less(ms.Length, 64 * 1024);
        using var request = GrpcHttpHelper.Create(method.FullName);
        request.Headers.Add(GrpcProtocolConstants.MessageEncodingHeader, encoding);
        request.Content = new GrpcStreamContent(ms);

        using var response = await Fixture.Client.SendAsync(request).DefaultTimeout();

        response.AssertTrailerStatus(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
        Assert.IsFalse(invoked);
        AssertHasLogRpcConnectionError(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
    }

    [Test]
    public async Task ReceivedMessageExceedsSize_ThrowError()
    {
        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                writeContext.EventId.Name == "ErrorReadingMessage" &&
                writeContext.State.ToString() == "Error reading message." &&
                GetRpcExceptionDetail(writeContext.Exception) == "Received message exceeds the maximum configured message size.")
            {
                return true;
            }

            return false;
        });

        var requestMessage = new HelloRequest
        {
            Name = "World" + new string('!', 64 * 1024)
        };

        var ms = new MemoryStream();
        MessageHelpers.WriteMessage(ms, requestMessage);

        // Act
        var response = await Fixture.Client.PostAsync(
            "Greet.Greeter/SayHello",
            new GrpcStreamContent(ms)).DefaultTimeout();

        // Assert
        response.AssertTrailerStatus(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");

        AssertHasLogRpcConnectionError(StatusCode.ResourceExhausted, "Received message exceeds the maximum configured message size.");
    }

    [Test]
    public async Task SentMessageExceedsSize_ThrowError()
    {
        // Arrange
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.LoggerName == TestConstants.ServerCallHandlerTestName &&
                writeContext.EventId.Name == "ErrorSendingMessage" &&
                writeContext.State.ToString() == "Error sending message." &&
                GetRpcExceptionDetail(writeContext.Exception) == "Sending message exceeds the maximum configured message size.")
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

        // Act
        var response = await Fixture.Client.PostAsync(
            "Greet.Greeter/SayHelloSendLargeReply",
            new GrpcStreamContent(ms)).DefaultTimeout();

        // Assert
        response.AssertTrailerStatus(StatusCode.ResourceExhausted, "Sending message exceeds the maximum configured message size.");

        AssertHasLogRpcConnectionError(StatusCode.ResourceExhausted, "Sending message exceeds the maximum configured message size.");
    }
}
