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
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Web;
using Grpc.Shared;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer
{
    [TestFixture]
    public class ConnectionTests : FunctionalTestBase
    {
#if NET5_0_OR_GREATER
        [Test]
        public async Task Active_UnaryCall_ConnectTimeout_ErrorThrownWhenTimeoutExceeded()
        {
            // Ignore errors
            SetExpectedErrorsFilter(writeContext =>
            {
                return true;
            });

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? host = null;
            async Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
            {
                var protocol = context.GetHttpContext().Request.Protocol;

                Logger.LogInformation("Received protocol: " + protocol);

                await tcs.Task;
                host = context.Host;
                return new HelloReply { Message = request.Name };
            }

            // Arrange
            using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod));

            var handler = new SocketsHttpHandler()
            {
                // ConnectTimeout is so small that CT will always be canceled before Socket is used.
                ConnectTimeout = TimeSpan.FromTicks(1),
            };
            var channel = GrpcChannel.ForAddress(endpoint.Address, new GrpcChannelOptions()
            {
                HttpHandler = handler,
                LoggerFactory = LoggerFactory
            });

            var client = TestClientFactory.Create(channel, endpoint.Method);

            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.UnaryCall(new HelloRequest()).ResponseAsync).DefaultTimeout();
            Assert.AreEqual("A connection could not be established within the configured ConnectTimeout.", ex.Status.DebugException!.Message);
        }

        [Test]
        public async Task Active_UnaryCall_MultipleStreams_UnavailableAddress_FallbackToWorkingAddress()
        {
            // Ignore errors
            SetExpectedErrorsFilter(writeContext =>
            {
                return true;
            });

            var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            string? host = null;
            async Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
            {
                var protocol = context.GetHttpContext().Request.Protocol;

                Logger.LogInformation("Received protocol: " + protocol);

                await tcs.Task;
                host = context.Host;
                return new HelloReply { Message = request.Name };
            }

            // Arrange
            using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);
            using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);

            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
            {
                new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
                new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
            }));

            var socketsHttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions() { RemoteCertificateValidationCallback = (_, __, ___, ____) => true }
            };
            var grpcWebHandler = new GrpcWebHandler(GrpcWebMode.GrpcWeb, new RequestVersionHandler(socketsHttpHandler));
            var channel = GrpcChannel.ForAddress("static:///localhost", new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = grpcWebHandler,
                ServiceProvider = services.BuildServiceProvider(),
                Credentials = new SslCredentials()
            });

            var client = TestClientFactory.Create(channel, endpoint1.Method);

            // Act
            grpcWebHandler.HttpVersion = new Version(1, 1);
            var http11CallTasks = new List<Task<HelloReply>>();
            for (int i = 0; i < 10; i++)
            {
                Logger.LogInformation($"Sending gRPC call {i}");

                http11CallTasks.Add(client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync);
            }

            Logger.LogInformation($"Done sending gRPC calls");

            var balancer = BalancerHelpers.GetInnerLoadBalancer<PickFirstBalancer>(channel)!;
            var subchannel = balancer._subchannel!;
            var transport = (SocketConnectivitySubchannelTransport)subchannel.Transport;
            var activeStreams = transport.GetActiveStreams();

            // Assert
            Assert.AreEqual(HttpHandlerType.SocketsHttpHandler, channel.HttpHandlerType);

            await TestHelpers.AssertIsTrueRetryAsync(() =>
            {
                activeStreams = transport.GetActiveStreams();
                return activeStreams.Count == 10;
            }, "Wait for connections to start.");
            foreach (var t in activeStreams)
            {
                Assert.AreEqual(new DnsEndPoint("127.0.0.1", 50051), t.Address.EndPoint);
            }

            // Act
            grpcWebHandler.HttpVersion = new Version(2, 0);
            var http2CallTasks = new List<Task<HelloReply>>();
            for (int i = 0; i < 10; i++)
            {
                Logger.LogInformation($"Sending gRPC call {i}");

                http2CallTasks.Add(client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync);
            }

            Logger.LogInformation($"Done sending gRPC calls");

            // Assert
            await TestHelpers.AssertIsTrueRetryAsync(() =>
            {
                activeStreams = transport.GetActiveStreams();
                return activeStreams.Count == 11;
            }, "Wait for connections to start.");
            Assert.AreEqual(new DnsEndPoint("127.0.0.1", 50051), activeStreams[activeStreams.Count - 1].Address.EndPoint);

            tcs.SetResult(null);

            await Task.WhenAll(http11CallTasks).DefaultTimeout();
            await Task.WhenAll(http2CallTasks).DefaultTimeout();

            Assert.AreEqual(ConnectivityState.Ready, channel.State);

            Logger.LogInformation($"Closing {endpoint1}");
            endpoint1.Dispose();

            // There are still be 10 HTTP/1.1 connections because they aren't immediately removed
            // when the server is shutdown and connectivity is lost.
            await TestHelpers.AssertIsTrueRetryAsync(() =>
            {
                activeStreams = transport.GetActiveStreams();
                return activeStreams.Count == 10;
            }, "Wait for HTTP/2 connection to end.");

            grpcWebHandler.HttpVersion = new Version(1, 1);

            await Task.Delay(1000);

            Logger.LogInformation($"Starting failed call");
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync).DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, ex.StatusCode);

            // Removed by failed call.
            activeStreams = transport.GetActiveStreams();
            Assert.AreEqual(0, activeStreams.Count);
            Assert.AreEqual(ConnectivityState.Idle, channel.State);

            Logger.LogInformation($"Next call goes to fallback address.");
            var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.TimeoutAfter(TimeSpan.FromSeconds(20));
            Assert.AreEqual("Balancer", reply.Message);
            Assert.AreEqual("127.0.0.1:50052", host);

            activeStreams = transport.GetActiveStreams();
            Assert.AreEqual(1, activeStreams.Count);
            Assert.AreEqual(new DnsEndPoint("127.0.0.1", 50052), activeStreams[0].Address.EndPoint);
        }

        [Test]
        public async Task Client_CallCredentials_WithLoadBalancing_RoundtripToken()
        {
            // Arrange
            string? authorization = null;
            Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
            {
                authorization = context.RequestHeaders.GetValue("authorization");
                return Task.FromResult(new HelloReply { Message = request.Name });
            }
            var credentials = CallCredentials.FromInterceptor((context, metadata) =>
            {
                metadata.Add("Authorization", $"Bearer TEST");
                return Task.CompletedTask;
            });
            using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50051, UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);
            using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(50052, UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);

            var services = new ServiceCollection();
            services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
            {
                new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
                new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
            }));
            var socketsHttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12,
                    RemoteCertificateValidationCallback = (_, __, ___, ____) => true
                }
            };
            var channel = GrpcChannel.ForAddress("static:///localhost", new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                ServiceProvider = services.BuildServiceProvider(),
                Credentials = ChannelCredentials.Create(new SslCredentials(), credentials),
                HttpHandler = socketsHttpHandler
            });

            var client = TestClientFactory.Create(channel, endpoint1.Method);

            // Act
            var reply = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual("Bearer TEST", authorization);
            Assert.AreEqual("Balancer", reply.Message);
        }

        private class RequestVersionHandler : DelegatingHandler
        {
            public RequestVersionHandler(HttpMessageHandler innerHandler) : base(innerHandler)
            {
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

                return base.SendAsync(request, cancellationToken);
            }
        }
#endif
    }
}
#endif
