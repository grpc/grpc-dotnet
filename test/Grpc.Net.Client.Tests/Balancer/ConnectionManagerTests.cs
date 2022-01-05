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
using System.Threading.Channels;
using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ChannelState = Grpc.Net.Client.Balancer.ChannelState;

namespace Grpc.Net.Client.Tests.Balancer
{
    [TestFixture]
    public class ClientChannelTests
    {
        [Test]
        public async Task PickAsync_ChannelStateChangesWithWaitForReady_WaitsForCorrectEndpoint()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddNUnitLogger();
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var resolver = new TestResolver(loggerFactory);
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 80)
            });

            var transportFactory = new TestSubchannelTransportFactory();
            var clientChannel = new ConnectionManager(resolver, disableResolverServiceConfig: false, loggerFactory, transportFactory, Array.Empty<LoadBalancerFactory>());
            clientChannel.ConfigureBalancer(c => new RoundRobinBalancer(c, loggerFactory));

            // Act
            var pickTask1 = clientChannel.PickAsync(
                new PickContext { Request = new HttpRequestMessage() },
                waitForReady: true,
                CancellationToken.None).AsTask();

            await clientChannel.ConnectAsync(waitForReady: true, CancellationToken.None).DefaultTimeout();

            var result1 = await pickTask1.DefaultTimeout();

            // Assert
            Assert.AreEqual(new DnsEndPoint("localhost", 80), result1.Address!.EndPoint);

            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 80),
                new BalancerAddress("localhost", 81)
            });

            for (var i = 0; i < transportFactory.Transports.Count; i++)
            {
                transportFactory.Transports[i].UpdateState(ConnectivityState.TransientFailure);
            }

            var pickTask2 = clientChannel.PickAsync(
                new PickContext { Request = new HttpRequestMessage() },
                waitForReady: true,
                CancellationToken.None).AsTask().DefaultTimeout();

            Assert.IsFalse(pickTask2.IsCompleted);

            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 82)
            });

            var result2 = await pickTask2.DefaultTimeout();
            Assert.AreEqual(new DnsEndPoint("localhost", 82), result2.Address!.EndPoint);
        }

        [Test]
        public async Task PickAsync_WaitForReadyWithDrop_ThrowsError()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddNUnitLogger();
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            var resolver = new TestResolver(loggerFactory);
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 80)
            });

            var transportFactory = new TestSubchannelTransportFactory();
            var clientChannel = new ConnectionManager(resolver, disableResolverServiceConfig: false, loggerFactory, transportFactory, Array.Empty<LoadBalancerFactory>());
            clientChannel.ConfigureBalancer(c => new DropLoadBalancer(c));

            // Act
            _ = clientChannel.ConnectAsync(waitForReady: true, CancellationToken.None).ConfigureAwait(false);

            var pickTask = clientChannel.PickAsync(
                new PickContext { Request = new HttpRequestMessage() },
                waitForReady: true,
                CancellationToken.None).AsTask();

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => pickTask).DefaultTimeout();
            Assert.AreEqual(StatusCode.DataLoss, ex.StatusCode);
        }

        [Test]
        public async Task PickAsync_RetryWithDrop_ThrowsError()
        {
            // Arrange
            string? authority = null;
            var testMessageHandler = TestHttpMessageHandler.Create(async request =>
            {
                authority = request.RequestUri!.Authority;
                var reply = new HelloReply { Message = "Hello world" };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var services = new ServiceCollection();
            services.AddNUnitLogger();
            services.AddSingleton<TestResolver>();
            services.AddSingleton<ResolverFactory, TestResolverFactory>();
            DropLoadBalancer? loadBalancer = null;
            services.AddSingleton<LoadBalancerFactory>(new DropLoadBalancerFactory(c =>
            {
                loadBalancer = new DropLoadBalancer(c);
                return loadBalancer;
            }));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

            var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
            {
                o.Credentials = ChannelCredentials.Insecure;
                o.ServiceProvider = services.BuildServiceProvider();
                o.ServiceConfig = new ServiceConfig
                {
                    MethodConfigs =
                    {
                        new MethodConfig
                        {
                            Names = { MethodName.Default },
                            RetryPolicy = new RetryPolicy
                            {
                                MaxAttempts = 5,
                                InitialBackoff = TimeSpan.FromMinutes(10),
                                MaxBackoff = TimeSpan.FromMinutes(10),
                                BackoffMultiplier = 1,
                                RetryableStatusCodes = { StatusCode.DataLoss }
                            }
                        }
                    },
                    LoadBalancingConfigs = { new LoadBalancingConfig("drop") }
                };
            });

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions().WithWaitForReady(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DataLoss, ex.StatusCode);

            Assert.AreEqual(1, loadBalancer!.PickCount);
        }

        [Test]
        public async Task PickAsync_HedgingWithDrop_ThrowsError()
        {
            // Arrange
            string? authority = null;
            var testMessageHandler = TestHttpMessageHandler.Create(async request =>
            {
                authority = request.RequestUri!.Authority;
                var reply = new HelloReply { Message = "Hello world" };

                var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();

                return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
            });

            var services = new ServiceCollection();
            services.AddNUnitLogger();
            services.AddSingleton<TestResolver>();
            services.AddSingleton<ResolverFactory, TestResolverFactory>();
            DropLoadBalancer? loadBalancer = null;
            services.AddSingleton<LoadBalancerFactory>(new DropLoadBalancerFactory(c =>
            {
                loadBalancer = new DropLoadBalancer(c);
                return loadBalancer;
            }));
            services.AddSingleton<ISubchannelTransportFactory>(new TestSubchannelTransportFactory());

            var invoker = HttpClientCallInvokerFactory.Create(testMessageHandler, "test:///localhost", configure: o =>
            {
                o.Credentials = ChannelCredentials.Insecure;
                o.ServiceProvider = services.BuildServiceProvider();
                o.ServiceConfig = new ServiceConfig
                {
                    MethodConfigs =
                    {
                        new MethodConfig
                        {
                            Names = { MethodName.Default },
                            HedgingPolicy = new HedgingPolicy
                            {
                                MaxAttempts = 5,
                                HedgingDelay = TimeSpan.FromMinutes(10),
                                NonFatalStatusCodes = { StatusCode.DataLoss }
                            }
                        }
                    },
                    LoadBalancingConfigs = { new LoadBalancingConfig("drop") }
                };
            });

            // Act
            var call = invoker.AsyncUnaryCall<HelloRequest, HelloReply>(ClientTestHelpers.ServiceMethod, string.Empty, new CallOptions().WithWaitForReady(), new HelloRequest());

            // Assert
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.DataLoss, ex.StatusCode);

            Assert.AreEqual(1, loadBalancer!.PickCount);
        }

        [Test]
        public async Task UpdateAddresses_ConnectIsInProgress_InProgressConnectIsCanceledAndRestarted()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddNUnitLogger();
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            var testLogger = loggerFactory.CreateLogger(GetType());

            var resolver = new TestResolver(loggerFactory);
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 80)
            });

            var connectAddressesChannel = System.Threading.Channels.Channel.CreateUnbounded<DnsEndPoint>();

            var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

            var transportFactory = new TestSubchannelTransportFactory(async (s, c) =>
            {
                c.Register(state => ((SyncPoint)state!).Continue(), syncPoint);

                var connectAddress = s.GetAddresses().Single();
                testLogger.LogInformation("Writing connect address " + connectAddress);

                await connectAddressesChannel.Writer.WriteAsync(connectAddress.EndPoint, c);
                await syncPoint.WaitToContinue();

                c.ThrowIfCancellationRequested();
                return ConnectivityState.Ready;
            });
            var clientChannel = new ConnectionManager(resolver, disableResolverServiceConfig: false, loggerFactory, transportFactory, Array.Empty<LoadBalancerFactory>());
            clientChannel.ConfigureBalancer(c => new PickFirstBalancer(c, loggerFactory));

            // Act
            _ = clientChannel.ConnectAsync(waitForReady: true, CancellationToken.None).ConfigureAwait(false);

            var connectAddress1 = await connectAddressesChannel.Reader.ReadAsync().AsTask().DefaultTimeout();
            Assert.AreEqual(80, connectAddress1.Port);

            // Endpoints are unchanged so continue connecting...
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 80)
            });
            Assert.IsFalse(syncPoint.WaitToContinue().IsCompleted);

            // Endpoints change so cancellation + reconnect triggered
            resolver.UpdateAddresses(new List<BalancerAddress>
            {
                new BalancerAddress("localhost", 81)
            });

            await syncPoint.WaitToContinue().DefaultTimeout();

            var connectAddress2 = await connectAddressesChannel.Reader.ReadAsync().AsTask().DefaultTimeout();
            Assert.AreEqual(81, connectAddress2.Port);
        }

        private class DropLoadBalancer : LoadBalancer
        {
            private readonly IChannelControlHelper _controller;

            public DropLoadBalancer(IChannelControlHelper controller)
            {
                _controller = controller;
            }

            public int PickCount { get; private set; }

            public override void RequestConnection()
            {
            }

            public override void UpdateChannelState(ChannelState state)
            {
                _controller.UpdateState(new BalancerState(ConnectivityState.TransientFailure, new DropSubchannelPicker(this)));
            }

            private class DropSubchannelPicker : SubchannelPicker
            {
                private readonly DropLoadBalancer _loadBalancer;

                public DropSubchannelPicker(DropLoadBalancer loadBalancer)
                {
                    _loadBalancer = loadBalancer;
                }

                public override PickResult Pick(PickContext context)
                {
                    _loadBalancer.PickCount++;
                    return PickResult.ForDrop(new Status(StatusCode.DataLoss, string.Empty));
                }
            }
        }

        private class DropLoadBalancerFactory : LoadBalancerFactory
        {
            private readonly Func<IChannelControlHelper, DropLoadBalancer> _loadBalancerFunc;

            public DropLoadBalancerFactory(Func<IChannelControlHelper, DropLoadBalancer> loadBalancerFunc)
            {
                _loadBalancerFunc = loadBalancerFunc;
            }

            public override string Name => "drop";

            public override LoadBalancer Create(LoadBalancerOptions options)
            {
                return _loadBalancerFunc(options.Controller);
            }
        }
    }
}
#endif
