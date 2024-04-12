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
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer;

[TestFixture]
public class RoundRobinBalancerTests
{
    [Test]
    public async Task ChangeAddresses_HasReadySubchannel_OldSubchannelShutdown()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var transportFactory = new TestSubchannelTransportFactory();
        services.AddSingleton<TestResolver>();
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var resolver = serviceProvider.GetRequiredService<TestResolver>();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        await channel.ConnectAsync().DefaultTimeout();

        // Assert
        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);

        // Wait for TryConnect to be called so state is connected
        await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 81)
        });
        Assert.AreEqual(ConnectivityState.Shutdown, subchannels[0].State);

        var newSubchannels = channel.ConnectionManager.GetSubchannels();
        CollectionAssert.AreNotEqual(subchannels, newSubchannels);
        Assert.AreEqual(1, newSubchannels.Count);

        Assert.AreEqual(1, newSubchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 81), newSubchannels[0]._addresses[0].EndPoint);

        await channel.ConnectionManager.PickAsync(new PickContext { Request = new HttpRequestMessage() }, waitForReady: false, CancellationToken.None).AsTask().DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, newSubchannels[0].State);
    }

    [Test]
    public async Task ResolverError_HasReadySubchannel_SubchannelUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        var transportFactory = new TestSubchannelTransportFactory();
        services.AddSingleton<TestResolver>();
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var resolver = serviceProvider.GetRequiredService<TestResolver>();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        await channel.ConnectAsync().DefaultTimeout();

        // Assert
        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);

        // Wait for TryConnect to be called so state is connected
        await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        resolver.UpdateError(new Status(StatusCode.Internal, "Error!", new Exception("Test exception!")));
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        // Existing channel continutes to run
        var newSubchannels = channel.ConnectionManager.GetSubchannels();
        CollectionAssert.AreEqual(subchannels, newSubchannels);
        Assert.AreEqual(1, newSubchannels.Count);

        await channel.ConnectionManager.PickAsync(new PickContext { Request = new HttpRequestMessage() }, waitForReady: false, CancellationToken.None).AsTask().DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, newSubchannels[0].State);
    }

    [Test]
    public async Task ResolverError_HasFailedSubchannel_SubchannelShutdown()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();
        services.AddSingleton<TestResolver>();
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) => Task.FromResult(new TryConnectResult(ConnectivityState.TransientFailure)));
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var resolver = serviceProvider.GetRequiredService<TestResolver>();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        _ = channel.ConnectAsync();

        // Assert
        await resolver.HasResolvedTask.DefaultTimeout();

        var subchannels = channel.ConnectionManager.GetSubchannels();

        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);

        await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.TransientFailure, subchannels[0].State);

        resolver.UpdateError(new Status(StatusCode.Internal, "Error!", new Exception("Test exception!")));
        Assert.AreEqual(ConnectivityState.Shutdown, subchannels[0].State);

        var pickContext = new PickContext { Request = new HttpRequestMessage() };
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await channel.ConnectionManager.PickAsync(pickContext, waitForReady: false, CancellationToken.None)).DefaultTimeout();
        Assert.AreEqual(StatusCode.Internal, ex.StatusCode);
        Assert.AreEqual("Error!", ex.Status.Detail);
        Assert.AreEqual("Test exception!", ex.Status.DebugException!.Message);
    }

    [Test]
    public async Task HasSubchannels_SubchannelStatusChanges_RefreshResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        ILogger logger = null!;
        SyncPoint? syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        var connectState = ConnectivityState.Ready;

        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            logger.LogInformation($"Transport factory returning state: {connectState}");
            return Task.FromResult(new TryConnectResult(connectState));
        });
        services.AddSingleton<TestResolver>(s =>
        {
            return new TestResolver(
                s.GetRequiredService<ILoggerFactory>(),
                async () =>
                {
                    logger.LogInformation("Resolver waiting to continue.");
                    await syncPoint.WaitToContinue().DefaultTimeout();

                    logger.LogInformation("Resolver creating new sync point.");
                    syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
                });
        });
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();

        logger = serviceProvider.GetRequiredService<ILogger<RoundRobinBalancerTests>>();
        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var resolver = serviceProvider.GetRequiredService<TestResolver>();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80)
        });

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);

        logger.LogInformation("Client connecting");
        var connectTask = channel.ConnectAsync();

        // Assert
        syncPoint!.Continue();
        logger.LogInformation("Client waiting for connect to complete.");
        await connectTask.DefaultTimeout();

        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(1, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);

        await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

        logger.LogInformation("Wait for the internal resolve task to be completed before triggering refresh again.");
        await resolver._resolveTask.DefaultTimeout();

        logger.LogInformation("Transport factory updating state.");
        connectState = ConnectivityState.TransientFailure;
        transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);

        // Transport will refresh resolver after some failures
        logger.LogInformation("Waiting for sync point in resolver.");
        await syncPoint!.WaitForSyncPoint().DefaultTimeout();
        syncPoint.Continue();
    }

    [Test]
    public async Task HasSubchannels_ResolverRefresh_MatchingSubchannelUnchanged()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddNUnitLogger();

        SyncPoint? syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        var connectState = ConnectivityState.Ready;

        var subChannelConnections = new List<Subchannel>();
        var transportFactory = TestSubchannelTransportFactory.Create((s, c) =>
        {
            lock (subChannelConnections)
            {
                subChannelConnections.Add(s);
            }
            return Task.FromResult(new TryConnectResult(connectState));
        });
        services.AddSingleton<TestResolver>(s =>
        {
            return new TestResolver(
                s.GetRequiredService<ILoggerFactory>(),
                async () =>
                {
                    await syncPoint.WaitToContinue().DefaultTimeout();
                    syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
                });
        });
        services.AddSingleton<ResolverFactory, TestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory>(transportFactory);
        var serviceProvider = services.BuildServiceProvider();

        var handler = new TestHttpMessageHandler((r, ct) => default!);
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
            ServiceProvider = serviceProvider,
            HttpHandler = handler
        };

        var resolver = serviceProvider.GetRequiredService<TestResolver>();
        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 80),
            new BalancerAddress("localhost", 81),
            new BalancerAddress("localhost", 82)
        });

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
        var connectTask = channel.ConnectAsync();

        // Assert
        syncPoint!.Continue();
        await connectTask.DefaultTimeout();

        var subchannels = channel.ConnectionManager.GetSubchannels();
        Assert.AreEqual(3, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(1, subchannels[1]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 81), subchannels[1]._addresses[0].EndPoint);
        Assert.AreEqual(1, subchannels[2]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 82), subchannels[2]._addresses[0].EndPoint);

        // Preserved because port 81, 82 is in both refresh results
        var discardedSubchannel = subchannels[0];
        var preservedSubchannel1 = subchannels[1];
        var preservedSubchannel2 = subchannels[2];

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType()),
            channel.ConnectionManager,
            expectedCount: 3).DefaultTimeout();

        var address2 = new BalancerAddress("localhost", 82);
        address2.Attributes.Set(new BalancerAttributesKey<int>("test"), 1);

        resolver.UpdateAddresses(new List<BalancerAddress>
        {
            new BalancerAddress("localhost", 81),
            address2,
            new BalancerAddress("localhost", 83)
        });

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(
            serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(GetType()),
            channel.ConnectionManager,
            expectedCount: 3).DefaultTimeout();

        subchannels = channel.ConnectionManager.GetSubchannels();
        var newSubchannel = subchannels[2];
        Assert.AreEqual(3, subchannels.Count);

        Assert.AreEqual(1, subchannels[0]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 81), subchannels[0]._addresses[0].EndPoint);
        Assert.AreEqual(1, subchannels[1]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 82), subchannels[1]._addresses[0].EndPoint);
        Assert.AreEqual(1, subchannels[2]._addresses.Count);
        Assert.AreEqual(new DnsEndPoint("localhost", 83), subchannels[2]._addresses[0].EndPoint);

        Assert.AreSame(preservedSubchannel1, subchannels[0]);
        Assert.AreSame(preservedSubchannel2, subchannels[1]);

        // Test that the channel's address was updated with new attribute with new attributes.
        Assert.AreSame(preservedSubchannel2.CurrentAddress, address2);

        lock (subChannelConnections)
        {
            try
            {
                Assert.AreEqual(4, subChannelConnections.Count);
                Assert.Contains(discardedSubchannel, subChannelConnections);
                Assert.Contains(preservedSubchannel1, subChannelConnections);
                Assert.Contains(preservedSubchannel2, subChannelConnections);
                Assert.Contains(newSubchannel, subChannelConnections);
            }
            catch (Exception ex)
            {
                throw new Exception("Connected subchannels: " + Environment.NewLine + string.Join(Environment.NewLine, subChannelConnections), ex);
            }
        }
    }
}
#endif
