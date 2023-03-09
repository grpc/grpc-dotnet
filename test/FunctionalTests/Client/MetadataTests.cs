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

using System.Text;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server;

[TestFixture]
public class MetadataTests : FunctionalTestBase
{
    [Test]
    public async Task GetTrailers_UnaryMethodSetStatusWithTrailers_TrailersAvailableInClient()
    {
        Task<HelloReply> UnaryDeadlineExceeded(HelloRequest request, ServerCallContext context)
        {
            context.ResponseTrailers.Add(new Metadata.Entry("Name", "the value was empty"));
            context.ResponseTrailers.Add(new Metadata.Entry("grpc-status-details-bin", Encoding.UTF8.GetBytes("Hello world")));
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
        Assert.AreEqual(2, trailers.Count);
        Assert.AreEqual("the value was empty", trailers.GetValue("name"));
        Assert.AreEqual("Hello world", Encoding.UTF8.GetString(trailers.GetValueBytes("grpc-status-details-bin")!));

        Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.AreEqual("Validation failed", ex.Status.Detail);
        Assert.AreEqual(2, ex.Trailers.Count);
        Assert.AreEqual("the value was empty", ex.Trailers.GetValue("name"));
        Assert.AreEqual("Hello world", Encoding.UTF8.GetString(ex.Trailers.GetValueBytes("grpc-status-details-bin")!));
    }

    [Test]
    public async Task GetTrailers_UnaryMethodThrowsExceptionWithTrailers_TrailersAvailableInClient()
    {
        Task<HelloReply> UnaryDeadlineExceeded(HelloRequest request, ServerCallContext context)
        {
            var trailers = new Metadata();
            trailers.Add(new Metadata.Entry("Name", "the value was empty"));
            trailers.Add(new Metadata.Entry("grpc-status-details-bin", Encoding.UTF8.GetBytes("Hello world")));
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
        Assert.GreaterOrEqual(trailers.Count, 2);
        Assert.AreEqual("the value was empty", trailers.GetValue("name"));
        Assert.AreEqual("Hello world", Encoding.UTF8.GetString(trailers.GetValueBytes("grpc-status-details-bin")!));

        Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.AreEqual("Validation failed", ex.Status.Detail);
        Assert.GreaterOrEqual(ex.Trailers.Count, 2);
        Assert.AreEqual("the value was empty", ex.Trailers.GetValue("name"));
        Assert.AreEqual("Hello world", Encoding.UTF8.GetString(ex.Trailers.GetValueBytes("grpc-status-details-bin")!));

        AssertHasLogRpcConnectionError(StatusCode.InvalidArgument, "Validation failed");
    }

    [Test]
    public async Task GetTrailers_ServerStreamingMethodSetStatusWithTrailers_TrailersAvailableInClient()
    {
        async Task UnaryDeadlineExceeded(HelloRequest request, IAsyncStreamWriter<HelloReply> writer, ServerCallContext context)
        {
            await writer.WriteAsync(new HelloReply());

            context.ResponseTrailers.Add(new Metadata.Entry("Name", "the value was empty"));
            context.Status = new Status(StatusCode.InvalidArgument, "Validation failed");
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

            return false;
        });

        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(UnaryDeadlineExceeded);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new HelloRequest());

        await call.ResponseStream.MoveNext().DefaultTimeout();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();

        // Assert
        var trailers = call.GetTrailers();
        Assert.AreEqual(1, trailers.Count);
        Assert.AreEqual("the value was empty", trailers.GetValue("name"));

        Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.AreEqual("Validation failed", ex.Status.Detail);
        Assert.AreEqual(1, ex.Trailers.Count);
        Assert.AreEqual("the value was empty", ex.Trailers.GetValue("name"));
    }

    [Test]
    public async Task GetTrailers_ServerStreamingMethodThrowsExceptionWithTrailers_TrailersAvailableInClient()
    {
        async Task UnaryDeadlineExceeded(HelloRequest request, IAsyncStreamWriter<HelloReply> writer, ServerCallContext context)
        {
            await writer.WriteAsync(new HelloReply());

            var trailers = new Metadata();
            trailers.Add(new Metadata.Entry("Name", "the value was empty"));
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Validation failed"), trailers);
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

            return false;
        });

        var method = Fixture.DynamicGrpc.AddServerStreamingMethod<HelloRequest, HelloReply>(UnaryDeadlineExceeded);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.ServerStreamingCall(new HelloRequest());

        await call.ResponseStream.MoveNext().DefaultTimeout();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext()).DefaultTimeout();

        // Assert
        var trailers = call.GetTrailers();
        Assert.GreaterOrEqual(trailers.Count, 1);
        Assert.AreEqual("the value was empty", trailers.GetValue("name"));

        Assert.AreEqual(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.AreEqual("Validation failed", ex.Status.Detail);
        Assert.GreaterOrEqual(ex.Trailers.Count, 1);
        Assert.AreEqual("the value was empty", ex.Trailers.GetValue("name"));

        AssertHasLogRpcConnectionError(StatusCode.InvalidArgument, "Validation failed");
    }

    [Test]
    public async Task ServerTrailers_UnaryMethodThrowsExceptionWithInvalidTrailers_FriendlyServerError()
    {
        Task<HelloReply> UnaryCall(HelloRequest request, ServerCallContext context)
        {
            var trailers = new Metadata();
            trailers.Add(new Metadata.Entry("Name", "This is invalid: \u0011"));
            return Task.FromException<HelloReply>(new RpcException(new Status(StatusCode.InvalidArgument, "Validation failed"), trailers));
        }

        // Arrange
        SetExpectedErrorsFilter(writeContext => true);

        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryCall);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new HelloRequest());

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Unknown, ex.StatusCode);
        Assert.AreEqual("Bad gRPC response. HTTP status code: 500", ex.Status.Detail);

        HasLogException(ex => ex.Message == "Error adding response trailer 'name'." && ex.InnerException!.Message == "Invalid non-ASCII or control character in header: 0x0011");
    }

    [Test]
    public async Task ServerHeaders_UnaryMethodThrowsExceptionWithInvalidTrailers_FriendlyServerError()
    {
        async Task<HelloReply> UnaryCall(HelloRequest request, ServerCallContext context)
        {
            var headers = new Metadata();
            headers.Add(new Metadata.Entry("Name", "This is invalid: \u0011"));
            await context.WriteResponseHeadersAsync(headers);
            return new HelloReply();
        }

        // Arrange
        SetExpectedErrorsFilter(writeContext => true);

        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryCall);

        var channel = CreateChannel();

        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new HelloRequest());

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        // Assert
        Assert.AreEqual(StatusCode.Unknown, ex.StatusCode);
        Assert.AreEqual("Exception was thrown by handler. InvalidOperationException: Error adding response header 'name'. InvalidOperationException: Invalid non-ASCII or control character in header: 0x0011", ex.Status.Detail);

        HasLogException(ex => ex.Message == "Error adding response header 'name'." && ex.InnerException!.Message == "Invalid non-ASCII or control character in header: 0x0011");
    }
}
