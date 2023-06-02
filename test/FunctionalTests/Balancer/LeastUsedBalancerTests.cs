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
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer;

[TestFixture]
public class LeastUsedBalancerTests : FunctionalTestBase
{
    [Test]
    public async Task UnaryCall_MultipleCalls_PickLeastUsed()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        SyncPoint? syncPoint = null;
        string? host = null;
        async Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            if (syncPoint != null)
            {
                await syncPoint.WaitToContinue();
            }

            return new HelloReply { Message = request.Name };
        }

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new LoadBalancingConfig("least_used"), new[] { endpoint1.Address, endpoint2.Address }, connect: true);

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(
            Logger,
            channel,
            expectedCount: 2,
            getPickerSubchannels: picker => (picker as LeastUsedPicker)?._subchannels.ToArray() ?? Array.Empty<Subchannel>()).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual("127.0.0.1:50051", host);

        // Act
        reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual("127.0.0.1:50051", host);

        // Act
        var sp1 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        using var pendingCall1 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50051", host);

        // Act
        var sp2 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        using var pendingCall2 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50052", host);

        // Act
        var sp3 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        using var pendingCall3 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50051", host);

        sp1.Continue();
        sp2.Continue();
        sp3.Continue();
    }

    [Test]
    public async Task UnaryCall_FlushHeaders_MultipleCalls_ClientAbort_PickLeastUsed()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        SyncPoint? syncPoint = null;
        string? host = null;
        async Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(Metadata.Empty);

            host = context.Host;
            if (syncPoint != null)
            {
                await syncPoint.WaitToContinue();
            }

            return new HelloReply { Message = request.Name };
        }

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new LoadBalancingConfig("least_used"), new[] { endpoint1.Address, endpoint2.Address }, connect: true);

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(
            Logger,
            channel,
            expectedCount: 2,
            getPickerSubchannels: picker => (picker as LeastUsedPicker)?._subchannels.ToArray() ?? Array.Empty<Subchannel>()).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var sp1 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        using var pendingCall1 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        await pendingCall1.ResponseHeadersAsync.DefaultTimeout();
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50051", host);

        // Act
        var sp2 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        using var pendingCall2 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        await pendingCall2.ResponseHeadersAsync.DefaultTimeout();
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50052", host);

        var picker = (LeastUsedPicker)channel.ConnectionManager._picker!;
        Assert.True(picker._subchannels[0].Attributes.TryGetValue(LeastUsedPicker.CounterKey, out var counter1));
        Assert.True(picker._subchannels[1].Attributes.TryGetValue(LeastUsedPicker.CounterKey, out var counter2));

        Assert.AreEqual(1, counter1!.Value);
        Assert.AreEqual(1, counter2!.Value);

        pendingCall2.Dispose();

        Assert.AreEqual(0, counter2!.Value);

        // Act
        var sp3 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        using var pendingCall3 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50052", host);

        sp1.Continue();
        sp2.Continue();
        sp3.Continue();
    }

    [Test]
    public async Task ServerStreamingCall_FlushHeaders_MultipleCalls_ServerAbort_PickLeastUsed()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        SyncPoint? syncPoint = null;
        string? host = null;
        async Task ServerStreamingMethod(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(Metadata.Empty);

            host = context.Host;
            if (syncPoint != null)
            {
                await syncPoint.WaitToContinue();
                Logger.LogInformation("Server aborting");
                context.GetHttpContext().Abort();
                return;
            }

            await responseStream.WriteAsync(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, ServerStreamingMethod, nameof(ServerStreamingMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, ServerStreamingMethod, nameof(ServerStreamingMethod));

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new LoadBalancingConfig("least_used"), new[] { endpoint1.Address, endpoint2.Address }, connect: true);

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(
            Logger,
            channel,
            expectedCount: 2,
            getPickerSubchannels: picker => (picker as LeastUsedPicker)?._subchannels.ToArray() ?? Array.Empty<Subchannel>()).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var sp1 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var pendingCall1 = client.ServerStreamingCall(new HelloRequest { Name = "Balancer" });
        await pendingCall1.ResponseHeadersAsync.DefaultTimeout();
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50051", host);

        // Act
        var sp2 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var pendingCall2 = client.ServerStreamingCall(new HelloRequest { Name = "Balancer" });
        await pendingCall2.ResponseHeadersAsync.DefaultTimeout();
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50052", host);

        var rs = pendingCall2.ResponseStream;
        _ = rs.MoveNext();

        var picker = (LeastUsedPicker)channel.ConnectionManager._picker!;
        Assert.True(picker._subchannels[0].Attributes.TryGetValue(LeastUsedPicker.CounterKey, out var counter1));
        Assert.True(picker._subchannels[1].Attributes.TryGetValue(LeastUsedPicker.CounterKey, out var counter2));

        Assert.AreEqual(1, counter1!.Value);
        Assert.AreEqual(1, counter2!.Value);

        sp2.Continue();

        Logger.LogInformation("Client waiting for notification");
        await TestHelpers.AssertIsTrueRetryAsync(() => counter2!.Value == 0, "");

        // Act
        var sp3 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var pendingCall3 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual("127.0.0.1:50052", host);

        sp1.Continue();
        sp2.Continue();
        sp3.Continue();
    }
}
#endif
