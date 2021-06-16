﻿#region Copyright notice and license

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

#if HAVE_LOAD_BALANCING
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public class PickFirstBalancerTests
    {
        [Test]
        public async Task ChangeAddresses_HasReadySubchannel_OldSubchannelShutdown()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);
            Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

            resolver.UpdateEndPoints(new List<DnsEndPoint> { new DnsEndPoint("localhost", 81) });

            var newSubchannels = channel.ConnectionManager.GetSubchannels();
            CollectionAssert.AreEqual(subchannels, newSubchannels);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 81), subchannels[0]._addresses[0]);
            Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);
        }

        [Test]
        public async Task ResolverError_HasReadySubchannel_SubchannelUnchanged()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) });

            var transportFactory = new TestSubchannelTransportFactory();
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
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
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            var transportFactory = new TestSubchannelTransportFactory(s => Task.FromResult(ConnectivityState.TransientFailure));
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
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
        public async Task RequestConnection_InitialConnectionFails_ExponentialBackoff()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            SyncPoint syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            var connectivityState = ConnectivityState.TransientFailure;

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory(async s =>
            {
                await syncPoint.WaitToContinue();
                return connectivityState;
            }));

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            var connectTask = channel.ConnectAsync();

            // First connection fails
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            syncPoint.Continue();
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            // TODO
            //Assert.IsFalse(connectTask.IsCompleted);

            // Second connection succeeds
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            connectivityState = ConnectivityState.Ready;
            syncPoint.Continue();

            await connectTask.DefaultTimeout();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);
            Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);
        }

        [Test]
        public async Task RequestConnection_InitialConnectionEnds_EntersIdleState()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint>
            {
                new DnsEndPoint("localhost", 80)
            });

            var transportConnectCount = 0;
            var transportFactory = new TestSubchannelTransportFactory(s =>
            {
                transportConnectCount++;
                return Task.FromResult(ConnectivityState.Ready);
            });

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, subchannels[0]._addresses.Count);
            Assert.AreEqual(new DnsEndPoint("localhost", 80), subchannels[0]._addresses[0]);
            Assert.AreEqual(ConnectivityState.Ready, subchannels[0].State);

            var stateChangedTask = channel.WaitForStateChangedAsync(ConnectivityState.Ready);

            transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);

            await stateChangedTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Idle, channel.State);

            Assert.AreEqual(1, transportConnectCount);
        }

        [Test]
        public async Task RequestConnection_IdleConnectionConnectAsync_StateToReady()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) });

            var transportConnectCount = 0;
            var transportFactory = new TestSubchannelTransportFactory(s =>
            {
                transportConnectCount++;
                return Task.FromResult(ConnectivityState.Ready);
            });

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync().DefaultTimeout();

            transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);

            Assert.AreEqual(ConnectivityState.Idle, channel.State);

            var stateChangedTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);

            await channel.ConnectAsync().DefaultTimeout();

            await stateChangedTask.DefaultTimeout();

            Assert.AreEqual(2, transportConnectCount);
        }

        [Test]
        public async Task RequestConnection_IdleConnectionPick_StateToReady()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging(b => b.AddProvider(new NUnitLoggerProvider()));

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) });

            var transportConnectCount = 0;
            var transportFactory = new TestSubchannelTransportFactory(s =>
            {
                transportConnectCount++;
                return Task.FromResult(ConnectivityState.Ready);
            });

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(transportFactory);

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync();

            transportFactory.Transports.Single().UpdateState(ConnectivityState.Idle);
            Assert.AreEqual(ConnectivityState.Idle, channel.State);

            var stateChangedTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);

            var pick = await channel.ConnectionManager.PickAsync(
                new PickContext { Request = new HttpRequestMessage() },
                waitForReady: false,
                CancellationToken.None).AsTask().DefaultTimeout();

            await stateChangedTask.DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, channel.State);
            Assert.AreEqual(2, transportConnectCount);
            Assert.AreEqual("localhost", pick.EndPoint.Host);
            Assert.AreEqual(80, pick.EndPoint.Port);
        }
    }
}
#endif
