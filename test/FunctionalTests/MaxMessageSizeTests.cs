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

using System.IO;
using System.Threading.Tasks;
using FunctionalTestsWebsite.Services;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
{
    [TestFixture]
    public class MaxMessageSizeTests : FunctionalTestBase
    {
        [Test]
        public async Task ReceivedMessageExceedsSize_ThrowError()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(GreeterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'ResourceExhausted' raised." &&
                       GetRpcExceptionDetail(writeContext.Exception) == "Received message exceeds the maximum configured message size.";
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
        }

        [Test]
        public async Task SentMessageExceedsSize_ThrowError()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                return writeContext.LoggerName == typeof(GreeterService).FullName &&
                       writeContext.EventId.Name == "RpcConnectionError" &&
                       writeContext.State.ToString() == "Error status code 'ResourceExhausted' raised." &&
                       GetRpcExceptionDetail(writeContext.Exception) == "Sending message exceeds the maximum configured message size.";
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
        }
    }
}
