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
using System.Net.Http;
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
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer
{
    [TestFixture]
    public class RoundRobinBalancerTests
    {
        [Test]
        public async Task ChangeAddresses_HasReadySubchannel_OldSubchannelShutdown()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddNUnitLogger();

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            var transportFactory = new TestSubchannelTransportFactory();
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync().DefaultTimeout();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);

            // Wait for TryConnect to be called so state is connected
            await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 81)
            });
            Assert.AreEqual(ConnectivityState.Shutdown, subchannels[0].State);

            var newSubchannels = channel.ConnectionManager.GetSubchannels();
            CollectionAssert.AreNotEqual(subchannels, newSubchannels);
            Assert.AreEqual(1, newSubchannels.Count);

            Assert.AreEqual(1, newSubchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 81), newSubchannels[0]._addresses[0]);

            await channel.ConnectionManager.PickAsync(new PickContext { Request = new HttpRequestMessage() }, waitForReady: false, CancellationToken.None).AsTask().DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, newSubchannels[0].State);
        }

        [Test]
        public async Task ResolverError_HasReadySubchannel_SubchannelUnchanged()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddNUnitLogger();

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            var transportFactory = new TestSubchannelTransportFactory();
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync().DefaultTimeout();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);

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

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            var transportFactory = new TestSubchannelTransportFactory((s, c) => Task.FromResult(ConnectivityState.TransientFailure));
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            _ = channel.ConnectAsync();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);

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

            SyncPoint? syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            var resolver = new TestResolver(async () =>
            {
                await syncPoint.WaitToContinue().DefaultTimeout();
                syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            });
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            var connectState = ConnectivityState.Ready;

            var transportFactory = new TestSubchannelTransportFactory((s, c) => Task.FromResult(connectState));
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } },
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            var connectTask = channel.ConnectAsync();

            // Assert
            syncPoint!.Continue();
            await connectTask.DefaultTimeout();

            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);

            await transportFactory.Transports.Single().TryConnectTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

            connectState = ConnectivityState.TransientFailure;
            transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);

            // Transport will refresh resolver after some failures
            await syncPoint!.WaitForSyncPoint().DefaultTimeout();
            syncPoint.Continue();
        }
    }
}
#endif
