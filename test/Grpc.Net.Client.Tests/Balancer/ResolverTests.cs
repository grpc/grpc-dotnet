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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Grpc.Net.Client.Tests.Balancer
{
    [TestFixture]
    public class ResolverTests
    {
        [Test]
        public async Task Resolver_ResolveNameFromServices_Success()
        {
            // Arrange
            var services = new ServiceCollection();

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
        }

        [Test]
        public async Task Resolver_WaitForRefreshAsync_Success()
        {
            // Arrange
            var services = new ServiceCollection();
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var resolver = new TestResolver(() => tcs.Task);
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
            var connectTask = channel.ConnectAsync();

            // Assert
            Assert.IsFalse(connectTask.IsCompleted);

            tcs.SetResult(null);

            await connectTask.DefaultTimeout();

            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);
        }

        [Test]
        public async Task Resolver_NoServiceConfigInResult_LoadBalancerUnchanged()
        {
            await Resolver_ServiceConfigInResult(resolvedServiceConfig: null);
        }

        [Test]
        public async Task Resolver_EmptyServiceConfigInResult_LoadBalancerUnchanged()
        {
            await Resolver_ServiceConfigInResult(resolvedServiceConfig: new ServiceConfig());
        }

        [Test]
        public async Task Resolver_MatchingPolicyInResult_LoadBalancerUnchanged()
        {
            await Resolver_ServiceConfigInResult(resolvedServiceConfig: new ServiceConfig
            {
                LoadBalancingConfigs = { new PickFirstConfig() }
            });
        }

        private async Task Resolver_ServiceConfigInResult(ServiceConfig? resolvedServiceConfig)
        {
            // Arrange
            var services = new ServiceCollection();
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var resolver = new TestResolver(() => tcs.Task);
            var result = ResolverResult.ForResult(new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) }, resolvedServiceConfig);
            resolver.UpdateResult(result);

            var createdCount = 0;
            var test = new TestLoadBalancerFactory(
                LoadBalancingConfig.PickFirstPolicyName,
                c =>
                {
                    createdCount++;
                    return new PickFirstBalancer(c, NullLoggerFactory.Instance);
                });

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<LoadBalancerFactory>(test));

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

            // Assert
            Assert.IsFalse(connectTask.IsCompleted);

            tcs.SetResult(null);

            await connectTask.DefaultTimeout();

            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(1, createdCount);

            resolver.UpdateResult(result);

            Assert.AreEqual(1, createdCount);
        }

        [Test]
        public async Task Resolver_ServiceConfigInResult()
        {
            // Arrange
            var services = new ServiceCollection();
            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            var resolver = new TestResolver(() => tcs.Task);
            var result = ResolverResult.ForResult(new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) }, serviceConfig: null);
            resolver.UpdateResult(result);

            TestLoadBalancer? firstLoadBalancer = null;
            var firstLoadBalancerCreatedCount = 0;
            var firstLoadBalancerFactory = new TestLoadBalancerFactory(
                LoadBalancingConfig.PickFirstPolicyName,
                c =>
                {
                    firstLoadBalancerCreatedCount++;
                    firstLoadBalancer = new TestLoadBalancer(new PickFirstBalancer(c, NullLoggerFactory.Instance));
                    return firstLoadBalancer;
                });

            TestLoadBalancer? secondLoadBalancer = null;
            var secondLoadBalancerCreatedCount = 0;
            var secondLoadBalancerFactory = new TestLoadBalancerFactory(
                "custom",
                c =>
                {
                    secondLoadBalancerCreatedCount++;
                    secondLoadBalancer = new TestLoadBalancer(new PickFirstBalancer(c, NullLoggerFactory.Instance));
                    return secondLoadBalancer;
                });

            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            var currentConnectivityState = ConnectivityState.Ready;

            services.AddSingleton<ResolverFactory>(new TestResolverFactory(resolver));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory(async (s, c) =>
            {
                await syncPoint.WaitToContinue();
                return currentConnectivityState;
            }));
            services.Add(ServiceDescriptor.Singleton<LoadBalancerFactory>(firstLoadBalancerFactory));
            services.Add(ServiceDescriptor.Singleton<LoadBalancerFactory>(secondLoadBalancerFactory));

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

            // Assert
            Assert.IsFalse(connectTask.IsCompleted);

            tcs.SetResult(null);

            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);
            Assert.AreEqual(ConnectivityState.Connecting, subchannels[0].State);

            Assert.AreEqual(1, firstLoadBalancerCreatedCount);

            syncPoint!.Continue();

            var pick = await channel.ConnectionManager.PickAsync(new PickContext(), true, CancellationToken.None).AsTask().DefaultTimeout();
            Assert.AreEqual(80, pick.Address.Port);

            // Create new SyncPoint so new load balancer is waiting to connect
            syncPoint = new SyncPoint(runContinuationsAsynchronously: false);

            result = ResolverResult.ForResult(
                new List<DnsEndPoint> { new DnsEndPoint("localhost", 81) },
                serviceConfig: new ServiceConfig
                {
                    LoadBalancingConfigs = { new LoadBalancingConfig("custom") }
                });
            resolver.UpdateResult(result);

            Assert.AreEqual(1, firstLoadBalancerCreatedCount);
            Assert.AreEqual(1, secondLoadBalancerCreatedCount);

            // Old address is still used because new load balancer is connecting
            pick = await channel.ConnectionManager.PickAsync(new PickContext(), true, CancellationToken.None).AsTask().DefaultTimeout();
            Assert.AreEqual(80, pick.Address.Port);

            Assert.IsFalse(firstLoadBalancer!.Disposed);

            syncPoint!.Continue();

            // New address is used
            pick = await channel.ConnectionManager.PickAsync(new PickContext(), true, CancellationToken.None).AsTask().DefaultTimeout();
            Assert.AreEqual(81, pick.Address.Port);

            Assert.IsTrue(firstLoadBalancer!.Disposed);

            await connectTask.DefaultTimeout();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ResolverOptions_ResolveServiceConfig_LoadBalancerChangedIfNotDisabled(bool disabled)
        {
            // Arrange
            var services = new ServiceCollection();

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(
                new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) },
                new ServiceConfig
                {
                    LoadBalancingConfigs = { new RoundRobinConfig() }
                });

            ResolverOptions? resolverOptions = null;
            services.AddSingleton<ResolverFactory>(new TestResolverFactory(o =>
            {
                resolverOptions = o;
                return resolver;
            }));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

            var handler = new TestHttpMessageHandler((r, ct) => default!);
            var channelOptions = new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure,
                ServiceProvider = services.BuildServiceProvider(),
                HttpHandler = handler,
                DisableResolverServiceConfig = disabled
            };

            // Act
            var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);
            await channel.ConnectAsync();

            // Assert
            var subchannels = channel.ConnectionManager.GetSubchannels();
            Assert.AreEqual(1, subchannels.Count);

            Assert.AreEqual(disabled, resolverOptions!.DisableServiceConfig);

            if (disabled)
            {
                Assert.IsNotNull(GetInnerLoadBalancer<PickFirstBalancer>(channel));
            }
            else
            {
                Assert.IsNotNull(GetInnerLoadBalancer<RoundRobinBalancer>(channel));
            }
        }

        [Test]
        public async Task ResolveServiceConfig_UnknownPolicyName_LoadBalancerUnchanged()
        {
            // Arrange
            var services = new ServiceCollection();

            var resolver = new TestResolver();
            resolver.UpdateEndPoints(
                new List<DnsEndPoint> { new DnsEndPoint("localhost", 80) },
                new ServiceConfig
                {
                    LoadBalancingConfigs = { new LoadBalancingConfig("unknown!") }
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
            Assert.IsNotNull(GetInnerLoadBalancer<PickFirstBalancer>(channel));
        }

        public static T? GetInnerLoadBalancer<T>(GrpcChannel channel) where T : LoadBalancer
        {
            var balancer = (ChildHandlerLoadBalancer)channel.ConnectionManager._balancer!;
            return (T?)balancer._current?.LoadBalancer;
        }
    }
}
#endif
