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

using System.Linq;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class TrailerMetadataTests : FunctionalTestBase
    {
        [Test]
        public async Task GetTrailers_UnaryMethodSetStatusWithTrailers_TrailersAvailableInClient()
        {
            Task<HelloReply> UnaryDeadlineExceeded(HelloRequest request, ServerCallContext context)
            {
                context.ResponseTrailers.Add(new Metadata.Entry("Name", "the value was empty"));
                context.Status = new Status(StatusCode.InvalidArgument, "Validation failed");
                return Task.FromResult(new HelloReply());
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorReadingMessage" &&
                    writeContext.Message == "Error reading message.")
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "GrpcStatusError" &&
                    writeContext.Message == "Call failed with gRPC error status. Status code: 'InvalidArgument', Message: 'Validation failed'.")
                {
                    return true;
                }

                return false;
            });

            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryDeadlineExceeded);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new HelloRequest());

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            // Assert
            var trailers = call.GetTrailers();
            Assert.AreEqual(1, trailers.Count);
            Assert.AreEqual("the value was empty", trailers.Single(m => m.Key == "name").Value);

            Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
            Assert.AreEqual("Validation failed", ex.Status.Detail);
            Assert.AreEqual(1, ex.Trailers.Count);
            Assert.AreEqual("the value was empty", ex.Trailers.Single(m => m.Key == "name").Value);
        }

        [Test]
        public async Task GetTrailers_UnaryMethodThrowsExceptionWithTrailers_TrailersAvailableInClient()
        {
            Task<HelloReply> UnaryDeadlineExceeded(HelloRequest request, ServerCallContext context)
            {
                var trailers = new Metadata();
                trailers.Add(new Metadata.Entry("Name", "the value was empty"));
                return Task.FromException<HelloReply>(new RpcException(new Status(StatusCode.InvalidArgument, "Validation failed"), trailers));
            }

            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "ErrorReadingMessage" &&
                    writeContext.Message == "Error reading message.")
                {
                    return true;
                }

                if (writeContext.LoggerName == "Grpc.Net.Client.Internal.GrpcCall" &&
                    writeContext.EventId.Name == "GrpcStatusError" &&
                    writeContext.Message == "Call failed with gRPC error status. Status code: 'InvalidArgument', Message: 'Validation failed'.")
                {
                    return true;
                }

                if (writeContext.LoggerName == "SERVER Grpc.AspNetCore.Server.ServerCallHandler" &&
                    writeContext.EventId.Name == "RpcConnectionError" &&
                    writeContext.Message == "Error status code 'InvalidArgument' raised.")
                {
                    return true;
                }

                return false;
            });

            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryDeadlineExceeded);

            var channel = CreateChannel();

            var client = TestClientFactory.Create(channel, method);

            // Act
            var call = client.UnaryCall(new HelloRequest());

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

            // Assert
            var trailers = call.GetTrailers();
            Assert.GreaterOrEqual(trailers.Count, 1);
            Assert.AreEqual("the value was empty", trailers.Single(m => m.Key == "name").Value);

            Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
            Assert.AreEqual("Validation failed", ex.Status.Detail);
            Assert.GreaterOrEqual(ex.Trailers.Count, 1);
            Assert.AreEqual("the value was empty", ex.Trailers.Single(m => m.Key == "name").Value);
        }
    }
}
