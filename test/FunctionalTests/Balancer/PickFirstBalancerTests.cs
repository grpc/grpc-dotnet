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

#if SUPPORT_LOAD_BALANCING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using FunctionalTestsWebsite;
using Google.Protobuf;
using Greet;
using Grpc.AspNetCore.FunctionalTests;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Web;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer;

[TestFixture]
public class PickFirstBalancerTests : FunctionalTestBase
{
    [Test]
    public async Task UnaryCall_CallAfterConnectionTimeout_Success()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var connectCount = 0;
        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }, connectTimeout: TimeSpan.FromMilliseconds(200), socketConnect:
            async (socket, endpoint, cancellationToken) =>
            {
                if (Interlocked.Increment(ref connectCount) == 1)
                {
                    await Task.Delay(1000, cancellationToken);
                }
                await socket.ConnectAsync(endpoint, cancellationToken);
            }).DefaultTimeout();
        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync).DefaultTimeout();
        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);
        Assert.IsInstanceOf(typeof(TimeoutException), ex.InnerException);

        await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
    }

    [Test]
    public async Task UnaryCall_CallAfterCancellation_Success()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }).DefaultTimeout();
        var client = TestClientFactory.Create(channel, endpoint.Method);
        
        // Kill endpoint so client can't connect.
        endpoint.Dispose();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(0.5));
        cts.Token.Register(() => Logger.LogInformation("Cancellation token raised"));

        // Start call that is canceled while getting picker.
        await ExceptionAssert.ThrowsAsync<RpcException>(() => client.UnaryCall(
            new HelloRequest { Name = "Balancer" },
            new CallOptions(cancellationToken: cts.Token)).ResponseAsync).DefaultTimeout();

        // Restart endpoint.
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), explicitPort: endpoint.Address.Port);

        // Act
        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady(true)).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);
    }

    [Test]
    public async Task UnaryCall_ReconnectBetweenCalls_Success()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint.Address.Port}", host);

        Logger.LogInformation("Ending " + endpoint.Address);
        endpoint.Dispose();

        // Wait for client to change to idle state in reaction to server stopping.
        await BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel, ConnectivityState.Idle).DefaultTimeout();

        Logger.LogInformation("Restarting");
        using var endpointNew = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), explicitPort: endpoint.Address.Port);

        reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady()).ResponseAsync.DefaultTimeout();

        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpointNew.Address.Port}", host);
    }

    [Test]
    public async Task UnaryCall_SingleAddressFailure_RpcError()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint.Address.Port}", host);

        Logger.LogInformation("Ending " + endpoint.Address);
        endpoint.Dispose();

        // Wait for client to change to idle state in reaction to server stopping.
        await BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel, ConnectivityState.Idle).DefaultTimeout();

        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(
            () => client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync).DefaultTimeout();

        Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);

        // Sometimes SocketException is wrapped by HttpRequestException.
        Assert.IsTrue(HasExceptionInStack<SocketException>(ex.Status.DebugException), $"Unexpected exception: {ex.Status.DebugException}");

        static bool HasExceptionInStack<T>(Exception? ex)
        {
            while (ex != null)
            {
                if (ex is T)
                {
                    return true;
                }

                ex = ex.InnerException;
            }

            return false;
        }
    }

    [Test]
    public async Task UnaryCall_UnavailableAddress_FallbackToWorkingAddress()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint1.Address, endpoint2.Address }).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);

        Logger.LogInformation("Ending " + endpoint1.Address);
        endpoint1.Dispose();

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(Logger, channel, expectedCount: 1,
            getPickerSubchannels: picker =>
            {
                // We want a subchannel that has no current address
                if (picker is PickFirstPicker pickFirstPicker)
                {
                    var currentAddress = pickFirstPicker.Subchannel.CurrentAddress;
                    Logger.LogInformation($"Got {nameof(PickFirstPicker)} with subchannel current address: {currentAddress?.ToString() ?? "null"}");
                    if (currentAddress == null)
                    {
                        return new[] { pickFirstPicker.Subchannel };
                    }
                }
                return Array.Empty<Subchannel>();
            }).DefaultTimeout();

        reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint2.Address.Port}", host);
    }

    [Test]
    public async Task UnaryCall_SubchannelReturnsToIdle_ReconnectOnNewCall()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var socketsHttpHandler = new SocketsHttpHandler { PooledConnectionIdleTimeout = TimeSpan.FromSeconds(1) };
        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint1.Address, endpoint2.Address }, socketsHttpHandler).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);
        Assert.AreEqual(ConnectivityState.Ready, channel.State);

        // Wait for pooled connection to timeout and return to idle
        await BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel, ConnectivityState.Idle).DefaultTimeout();

        reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);
    }

    [Test]
    public async Task UnaryCall_MultipleStreams_UnavailableAddress_FallbackToWorkingAddress()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        object l = new object();
        int callsOnServer = 0;
        int callsToServer = 150;

        var allOnServerTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? host = null;
        async Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            lock (l)
            {
                callsOnServer++;
                if (callsOnServer == callsToServer)
                {
                    allOnServerTcs.SetResult(null);
                }
            }
            await tcs.Task;
            host = context.Host;
            return new HelloReply { Message = request.Name };
        }

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint1.Address, endpoint2.Address }).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var callTasks = new List<Task>();
        for (int i = 0; i < callsToServer; i++)
        {
            Logger.LogInformation($"Sending gRPC call {i}");

            callTasks.Add(client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync);
        }

        Logger.LogInformation($"Done sending gRPC calls");

        await allOnServerTcs.Task.DefaultTimeout().DefaultTimeout();

        Logger.LogInformation($"All gRPC calls on server");

        await BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel, ConnectivityState.Ready).DefaultTimeout();

        var balancer = BalancerHelpers.GetInnerLoadBalancer<PickFirstBalancer>(channel)!;
        var subchannel = balancer._subchannel!;
        var transport = (SocketConnectivitySubchannelTransport)subchannel.Transport;
        var activeStreams = transport.GetActiveStreams();

        // Assert
        Assert.GreaterOrEqual(activeStreams.Count, 2);
        foreach (var stream in activeStreams)
        {
            Assert.AreEqual(new DnsEndPoint("127.0.0.1", endpoint1.Address.Port), stream.EndPoint);
        }

        tcs.SetResult(null);

        Logger.LogInformation($"Wait for all tasks");
        await Task.WhenAll(callTasks).DefaultTimeout();

        Logger.LogInformation($"Remove endpoint");
        endpoint1.Dispose();

        await TestHelpers.AssertIsTrueRetryAsync(() =>
        {
            activeStreams = transport.GetActiveStreams();
            Logger.LogInformation($"Current active stream addresses: {string.Join(", ", activeStreams.Select(s => s.EndPoint))}");
            return activeStreams.Count == 0;
        }, "Active streams removed.", Logger).DefaultTimeout();

        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint2.Address.Port}", host);

        activeStreams = transport.GetActiveStreams();
        Assert.AreEqual(1, activeStreams.Count);
        Assert.AreEqual(new DnsEndPoint("127.0.0.1", endpoint2.Address.Port), activeStreams[0].EndPoint);
    }

    [Test]
    public async Task UnaryCall_NoServerAndDeadlineAndWaitForReady_DeadlineTimeout()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        endpoint.Dispose();

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }, connect: false).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Act
        var callOptions = new CallOptions();
        callOptions = callOptions.WithWaitForReady();
        callOptions = callOptions.WithDeadline(DateTime.UtcNow.Add(TimeSpan.FromMilliseconds(200)));

        var callTask = client.UnaryCall(new HelloRequest { Name = "Balancer" }, callOptions).ResponseAsync;

        // Assert
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => callTask).DefaultTimeout();
        Assert.AreEqual(StatusCode.DeadlineExceeded, ex.StatusCode);
    }

    [Test]
    public async Task UnaryCall_MultipleChannelsShareHandler_ReconnectBetweenCalls_Success()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        string? host = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        Logger.LogInformation("Starting server");
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        Logger.LogInformation("Creating clients");
        var socketsHttpHandler = new SocketsHttpHandler();
        var channel1 = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }, socketsHttpHandler).DefaultTimeout();
        var channel2 = await BalancerHelpers.CreateChannel(LoggerFactory, new PickFirstConfig(), new[] { endpoint.Address }, socketsHttpHandler).DefaultTimeout();

        var client1 = TestClientFactory.Create(channel1, endpoint.Method);
        var client2 = TestClientFactory.Create(channel2, endpoint.Method);

        // Act
        Logger.LogInformation("Starting calls");
        var reply1Task = client1.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        var reply2Task = client2.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

        // Assert
        Logger.LogInformation("Client waiting for replies");
        Assert.AreEqual("Balancer", (await reply1Task).Message);
        Assert.AreEqual("Balancer", (await reply2Task).Message);
        Assert.AreEqual($"127.0.0.1:{endpoint.Address.Port}", host);

        Logger.LogInformation("Ending " + endpoint.Address);
        endpoint.Dispose();

        await Task.WhenAll(
            BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel1, ConnectivityState.Idle, channelId: 1),
            BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel2, ConnectivityState.Idle, channelId: 2)).DefaultTimeout();

        Logger.LogInformation("Restarting");
        using var endpointNew = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), explicitPort: endpoint.Address.Port);

        reply1Task = client1.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady()).ResponseAsync.DefaultTimeout();
        reply2Task = client2.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady()).ResponseAsync.DefaultTimeout();

        Assert.AreEqual("Balancer", (await reply1Task).Message);
        Assert.AreEqual("Balancer", (await reply2Task).Message);
        Assert.AreEqual($"127.0.0.1:{endpointNew.Address.Port}", host);
    }
}
#endif
