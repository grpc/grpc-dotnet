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
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer;

[TestFixture]
public class LeastUsedBalancerTests : FunctionalTestBase
{
    [Test]
    public async Task UnaryCall_MultipleCalls_RoundRobin()
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
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

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
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);

        // Act
        reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
        // Assert
        Assert.AreEqual("Balancer", reply.Message);
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);

        // Act
        var sp1 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var pendingCall1 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);

        // Act
        var sp2 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var pendingCall2 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual($"127.0.0.1:{endpoint2.Address.Port}", host);

        // Act
        var sp3 = syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var pendingCall3 = client.UnaryCall(new HelloRequest { Name = "Balancer" });
        // Assert
        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        Assert.AreEqual($"127.0.0.1:{endpoint1.Address.Port}", host);

        sp1.Continue();
        sp2.Continue();
        sp3.Continue();
    }
}
#endif
