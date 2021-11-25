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
#if NET5_0_OR_GREATER

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

namespace Grpc.AspNetCore.FunctionalTests.Balancer
{
    [TestFixture]
    public class RoundRobinBalancerTests : FunctionalTestBase
    {
        [Test]
        public async Task DisconnectEndpoint_NoCallsMade_SubchannelStateUpdated()
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
            using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));

            var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { endpoint.Address });

            await channel.ConnectAsync().DefaultTimeout();

            var subchannel = (await BalancerHelpers.WaitForSubChannelsToBeReadyAsync(Logger, channel, 1).DefaultTimeout()).Single();

            // Act
            endpoint.Dispose();

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(
                () => subchannel.State == ConnectivityState.TransientFailure,
                "Wait for subchannel to fail.").DefaultTimeout();
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
            using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));

            var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { endpoint.Address });

            var client = TestClientFactory.Create(channel, endpoint.Method);

            // Act
            var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Balancer", reply.Message);
            Assert.AreEqual("127.0.0.1:50051", host);

            Logger.LogInformation("Ending " + endpoint.Address);
            endpoint.Dispose();

            Logger.LogInformation("Restarting");
            using var endpointNew = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));

            // Act
            reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Balancer", reply.Message);
            Assert.AreEqual("127.0.0.1:50051", host);
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
            using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));

            var socketsHttpHandler = new SocketsHttpHandler();
            var channel1 = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { endpoint.Address }, socketsHttpHandler);
            var channel2 = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { endpoint.Address }, socketsHttpHandler);

            var client1 = TestClientFactory.Create(channel1, endpoint.Method);
            var client2 = TestClientFactory.Create(channel2, endpoint.Method);

            // Act
            var reply1Task = client1.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
            var reply2Task = client2.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Balancer", (await reply1Task).Message);
            Assert.AreEqual("Balancer", (await reply2Task).Message);
            Assert.AreEqual("127.0.0.1:50051", host);

            // Wait for connecting or failure.
            // Connecting is faster to wait for, but the status could change so quickly that wait for state change is not triggered.
            // Use failure as backup status.
            var expectedStates = new[] { ConnectivityState.Connecting, ConnectivityState.TransientFailure };
            var waitForConnectingTask = Task.WhenAll(
                BalancerHelpers.WaitForChannelStatesAsync(Logger, channel1, expectedStates, channelId: 1),
                BalancerHelpers.WaitForChannelStatesAsync(Logger, channel2, expectedStates, channelId: 2));

            Logger.LogInformation("Ending " + endpoint.Address);
            endpoint.Dispose();

            await waitForConnectingTask.DefaultTimeout();

            Logger.LogInformation("Restarting");
            using var endpointNew = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));

            // Act
            reply1Task = client1.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady()).ResponseAsync.DefaultTimeout();
            reply2Task = client2.UnaryCall(new HelloRequest { Name = "Balancer" }, new CallOptions().WithWaitForReady()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Balancer", (await reply1Task).Message);
            Assert.AreEqual("Balancer", (await reply2Task).Message);
            Assert.AreEqual("127.0.0.1:50051", host);
        }

        [Test]
        public async Task UnaryCall_MultipleCalls_RoundRobin()
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
            using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));
            using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod));

            var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { endpoint1.Address, endpoint2.Address }, connect: true);

            await BalancerHelpers.WaitForSubChannelsToBeReadyAsync(Logger, channel, 2).DefaultTimeout();

            var client = TestClientFactory.Create(channel, endpoint1.Method);

            // Act
            var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
            // Assert
            Assert.AreEqual("Balancer", reply.Message);
            var nextHost = GetNextHost(host!);

            // Act
            reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
            // Assert
            Assert.AreEqual("Balancer", reply.Message);
            Assert.AreEqual(nextHost, host!);
            nextHost = GetNextHost(host!);

            // Act
            reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
            // Assert
            Assert.AreEqual("Balancer", reply.Message);
            Assert.AreEqual(nextHost, host);

            string GetNextHost(string host)
            {
                return host == "127.0.0.1:50051" ? "127.0.0.1:50052" : "127.0.0.1:50051";
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
            using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));
            using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod));

            var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { endpoint1.Address, endpoint2.Address }, connect: true);

            await BalancerHelpers.WaitForSubChannelsToBeReadyAsync(Logger, channel, 2).DefaultTimeout();

            var client = TestClientFactory.Create(channel, endpoint1.Method);

            var reply1 = await client.UnaryCall(new HelloRequest { Name = "Balancer1" });
            Assert.AreEqual("Balancer1", reply1.Message);
            var host1 = host;

            var reply2 = await client.UnaryCall(new HelloRequest { Name = "Balancer2" });
            Assert.AreEqual("Balancer2", reply2.Message);
            var host2 = host;

            Assert.Contains("127.0.0.1:50051", new[] { host1, host2 });
            Assert.Contains("127.0.0.1:50052", new[] { host1, host2 });

            endpoint1.Dispose();

            var subChannels = (await BalancerHelpers.WaitForSubChannelsToBeReadyAsync(Logger, channel, 1).DefaultTimeout()).Single();
            Assert.AreEqual(50052, subChannels.CurrentAddress?.EndPoint.Port);

            reply1 = await client.UnaryCall(new HelloRequest { Name = "Balancer" });
            Assert.AreEqual("Balancer", reply1.Message);
            Assert.AreEqual("127.0.0.1:50052", host);
        }

        [Test]
        public async Task Resolver_SubchannelTransientFailure_ResolverRefreshed()
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
            using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));
            using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod));

            SyncPoint? syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            syncPoint.Continue();

            var resolver = new TestResolver(async () =>
            {
                await syncPoint.WaitToContinue().DefaultTimeout();
                syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            });
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
                new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
            });

            var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), resolver, connect: true);

            await BalancerHelpers.WaitForSubChannelsToBeReadyAsync(Logger, channel, 2).DefaultTimeout();

            var client = TestClientFactory.Create(channel, endpoint1.Method);

            var waitForRefreshTask = syncPoint.WaitForSyncPoint();

            endpoint1.Dispose();

            await waitForRefreshTask.DefaultTimeout();

            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
            });

            syncPoint.Continue();

            await BalancerHelpers.WaitForSubChannelsToBeReadyAsync(Logger, channel, 1).DefaultTimeout();
        }
    }
}

#endif
#endif
