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

using System.Globalization;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client;

[TestFixture]
public class UnaryTests : FunctionalTestBase
{
    [Test]
    public async Task ThrowErrorWithOK_ClientThrowsFailedToDeserializeError()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            if (writeContext.State.ToString() == "Header contains an OK gRPC status. This is invalid for unary or client streaming calls because a status in the header indicates there is no response body. " +
                "A message in the response body is required for unary and client streaming calls.")
            {
                return true;
            }

            return false;
        });

        Task<HelloReply> UnaryThrowError(HelloRequest request, ServerCallContext context)
        {
            return Task.FromException<HelloReply>(new RpcException(new Status(StatusCode.OK, "Message")));
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryThrowError);
        var channel = CreateChannel();
        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new HelloRequest());

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();

        Assert.AreEqual(StatusCode.Internal, ex.Status.StatusCode);
        StringAssert.StartsWith("Failed to deserialize response message.", ex.Status.Detail);

        Assert.AreEqual(StatusCode.Internal, call.GetStatus().StatusCode);
        StringAssert.StartsWith("Failed to deserialize response message.", call.GetStatus().Detail);
    }

    [Test]
    public async Task WriteResponseHeadersAsync_HeadersSentEarly()
    {
        var tcs = new TaskCompletionSource<HelloReply>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<HelloReply> UnaryThrowError(HelloRequest request, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(new Metadata
            {
                new Metadata.Entry("key", "value")
            });

            return await tcs.Task;
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryThrowError);
        var channel = CreateChannel();
        var client = TestClientFactory.Create(channel, method);

        // Act
        var call = client.UnaryCall(new HelloRequest());

        // Assert
        var headers = await call.ResponseHeadersAsync.DefaultTimeout();
        Assert.AreEqual("value", headers.GetValue("key"));
        Assert.IsFalse(call.ResponseAsync.IsCompleted);

        tcs.SetResult(new HelloReply());
    }

    [TestCase("fr", "fr")]
    [TestCase(null, "en-US")]
    public async Task Unary_SetAcceptLanguage_ServerCultureChanged(string clientAcceptLanguage, string expectedServerCulture)
    {
        string? serverCulture = null;
        Task<HelloReply> UnaryThrowError(HelloRequest request, ServerCallContext context)
        {
            serverCulture = Thread.CurrentThread.CurrentCulture.Name;
            return Task.FromResult(new HelloReply { Message = serverCulture });
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryThrowError);
        var channel = CreateChannel();
        var client = TestClientFactory.Create(channel, method);
        var metadata = new Metadata();
        if (clientAcceptLanguage != null)
        {
            metadata.Add("accept-language", clientAcceptLanguage);
        }

        // Act
        var call = client.UnaryCall(new HelloRequest(), new CallOptions(headers: metadata));
        await call.ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(expectedServerCulture, serverCulture);
    }

    [Test]
    public async Task MaxConcurrentStreams_StartConcurrently_AdditionalConnectionsCreated()
    {
        await RunConcurrentStreams(writeResponseHeaders: false);
    }

    [Test]
    public async Task MaxConcurrentStreams_StartIndividually_AdditionalConnectionsCreated()
    {
        await RunConcurrentStreams(writeResponseHeaders: true);
    }

    private async Task RunConcurrentStreams(bool writeResponseHeaders)
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var streamCount = 210;
        var count = 0;
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectionIds = new List<string>();
        var l = new object();

        async Task<HelloReply> UnaryThrowError(HelloRequest request, ServerCallContext context)
        {
            lock (l)
            {
                count++;

                var connectionId = context.GetHttpContext().Connection.Id;
                if (!connectionIds.Contains(connectionId))
                {
                    connectionIds.Add(connectionId);
                }
            }

            Logger.LogInformation($"Received message '{request.Name}'");

            if (writeResponseHeaders)
            {
                await context.WriteResponseHeadersAsync(new Metadata());
            }

            if (count == streamCount)
            {
                tcs.SetResult(null);
            }

            await tcs.Task;

            return new HelloReply { Message = request.Name };
        }

        // Arrange
        var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryThrowError);

        var channel = GrpcChannel.ForAddress(Fixture.GetUrl(TestServerEndpointName.Http2), new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory
        });

        var client = TestClientFactory.Create(channel, method);

        var calls = new AsyncUnaryCall<HelloReply>[streamCount];
        try
        {
            // Act
            for (var i = 0; i < calls.Length; i++)
            {
                var call = client.UnaryCall(new HelloRequest { Name = (i + 1).ToString(CultureInfo.InvariantCulture) });
                calls[i] = call;

                if (writeResponseHeaders)
                {
                    await call.ResponseHeadersAsync.DefaultTimeout();
                }
            }

            await Task.WhenAll(calls.Select(c => c.ResponseHeadersAsync)).DefaultTimeout();

            // Assert
            Assert.AreEqual(3, connectionIds.Count);
        }
        catch (Exception ex)
        {
            throw new Exception($"Received {count} of {streamCount} on the server. Connection Ids: {string.Join(", ", connectionIds)}", ex);
        }
        finally
        {
            for (var i = 0; i < calls.Length; i++)
            {
                calls[i].Dispose();
            }
        }
    }

    [Test]
    public async Task Health_Check_Success()
    {
        // Arrange
        var client = new Health.V1.Health.HealthClient(Channel);

        // Act
        var result = await client.CheckAsync(new HealthCheckRequest());

        // Assert
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, result.Status);
    }
}
