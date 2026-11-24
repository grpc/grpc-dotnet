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
using System.Net;
using System.Net.Security;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Configuration;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer;

// Some tests change HttpClient.DefaultProxy, which is global to the process.
[TestFixture]
[NonParallelizable]
public class ProxyTests : FunctionalTestBase
{
    private static Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        return Task.FromResult(new HelloReply { Message = httpContext.Connection.LocalPort.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    private static SocketsHttpHandler CreateHandler(Action<SocketsHttpHandler>? configure = null)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, __, ___, ____) => true
            }
        };
        configure?.Invoke(handler);
        return handler;
    }

    private static async Task WithDefaultProxy(IWebProxy proxy, Func<Task> action)
    {
        var previous = HttpClient.DefaultProxy;
        HttpClient.DefaultProxy = proxy;
        try
        {
            await action();
        }
        finally
        {
            HttpClient.DefaultProxy = previous;
        }
    }

    private static string ConnectRequestLine(Uri address) => $"CONNECT {address.Host}:{address.Port} HTTP/1.1";

    [Test]
    public async Task DefaultProxy_HttpsAddress_ConnectsThroughProxy()
    {
        // Arrange
        using var proxyServer = LoopbackProxyServer.Create();
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), isHttps: true);

        await WithDefaultProxy(new WebProxy(proxyServer.Uri), async () =>
        {
            using var channel = GrpcChannel.ForAddress(endpoint.Address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = CreateHandler()
            });

            // The subchannel reports ready without connecting a socket to the address.
            await channel.ConnectAsync().DefaultTimeout();
            Assert.AreEqual(ConnectivityState.Ready, channel.State);

            var client = TestClientFactory.Create(channel, endpoint.Method);

            // Act
            var reply = await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(endpoint.Address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), reply.Message);
            Assert.AreEqual(1, proxyServer.Requests.Count);
            Assert.AreEqual(ConnectRequestLine(endpoint.Address), proxyServer.Requests[0].RequestLine);
        });
    }

    [Test]
    public async Task DefaultProxy_UseProxyFalse_ConnectsDirectly()
    {
        // Arrange
        using var proxyServer = LoopbackProxyServer.Create();
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), isHttps: true);

        await WithDefaultProxy(new WebProxy(proxyServer.Uri), async () =>
        {
            using var channel = GrpcChannel.ForAddress(endpoint.Address, new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = CreateHandler(h => h.UseProxy = false)
            });
            var client = TestClientFactory.Create(channel, endpoint.Method);

            // Act
            var reply = await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();

            // Assert
            Assert.AreEqual(endpoint.Address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), reply.Message);
            Assert.AreEqual(0, proxyServer.Connections);
        });
    }

    [Test]
    public async Task Proxy_BypassedAddress_ConnectsDirectly()
    {
        // Arrange
        using var proxyServer = LoopbackProxyServer.Create();
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), isHttps: true);

        // WebProxy.GetProxy returns the destination (not null) for a bypassed address.
        // The address is direct only because IsBypassed is checked.
        var proxy = new WebProxy(proxyServer.Uri) { BypassList = new[] { System.Text.RegularExpressions.Regex.Escape(endpoint.Address.Host) } };

        using var channel = GrpcChannel.ForAddress(endpoint.Address, new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory,
            HttpHandler = CreateHandler(h => h.Proxy = proxy)
        });
        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(endpoint.Address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), reply.Message);
        Assert.AreEqual(0, proxyServer.Connections);
    }

    [Test]
    public async Task RoundRobin_Proxy_ConnectsThroughProxyToEachAddress()
    {
        // Arrange
        using var proxyServer = LoopbackProxyServer.Create();
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), isHttps: true);
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), isHttps: true);

        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
        {
            new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
            new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
        }));

        using var channel = GrpcChannel.ForAddress("static:///localhost", new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory,
            HttpHandler = CreateHandler(h => h.Proxy = new WebProxy(proxyServer.Uri)),
            ServiceProvider = services.BuildServiceProvider(),
            Credentials = new SslCredentials(),
            ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } }
        });

        await channel.ConnectAsync().DefaultTimeout();
        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(Logger, channel, 2).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var ports = new HashSet<string>();
        for (var i = 0; i < 4; i++)
        {
            var reply = await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();
            ports.Add(reply.Message);
        }

        // Assert
        CollectionAssert.AreEquivalent(
            new[] { endpoint1.Address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), endpoint2.Address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ports);
        var requestLines = proxyServer.Requests.Select(r => r.RequestLine).ToList();
        CollectionAssert.AreEquivalent(new[] { ConnectRequestLine(endpoint1.Address), ConnectRequestLine(endpoint2.Address) }, requestLines);
    }

    // https://github.com/grpc/grpc-dotnet/issues/2075
    [Test]
    public async Task RoundRobin_DefaultProxy_UseProxyFalse_ConnectsDirectly()
    {
        // Arrange
        using var proxyServer = LoopbackProxyServer.Create();
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
        {
            new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
            new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
        }));

        await WithDefaultProxy(new WebProxy(proxyServer.Uri), async () =>
        {
            using var channel = GrpcChannel.ForAddress("static:///localhost", new GrpcChannelOptions
            {
                LoggerFactory = LoggerFactory,
                HttpHandler = CreateHandler(h => h.UseProxy = false),
                ServiceProvider = services.BuildServiceProvider(),
                Credentials = ChannelCredentials.Insecure,
                ServiceConfig = new ServiceConfig { LoadBalancingConfigs = { new RoundRobinConfig() } }
            });

            await channel.ConnectAsync().DefaultTimeout();
            await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(Logger, channel, 2).DefaultTimeout();

            var client = TestClientFactory.Create(channel, endpoint1.Method);

            // Act
            var ports = new HashSet<string>();
            for (var i = 0; i < 4; i++)
            {
                var reply = await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();
                ports.Add(reply.Message);
            }

            // Assert
            Assert.AreEqual(2, ports.Count);
            Assert.AreEqual(0, proxyServer.Connections);
        });
    }

    [Test]
    public async Task ProxiedAddress_SubchannelDoesNotConnectSocket()
    {
        // Arrange
        var socketConnectCount = 0;

        // The address can't be resolved, so it is only usable through a proxy.
        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            new[] { new Uri("http://unreachable.invalid:80") },
            socketConnect: (socket, endPoint, cancellationToken) =>
            {
                Interlocked.Increment(ref socketConnectCount);
                return socket.ConnectAsync(endPoint, cancellationToken);
            },
            isProxied: _ => true);

        // Act
        await channel.ConnectAsync().DefaultTimeout();

        // Assert
        Assert.AreEqual(ConnectivityState.Ready, channel.State);
        Assert.AreEqual(0, socketConnectCount);
    }

    [Test]
    public async Task ProxiedAddress_HandlerConnectsDirectly_CallSucceeds()
    {
        // Arrange
        // The subchannel predicts a proxy but the handler has none. The handler connects a new socket to the address.
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            new[] { endpoint.Address },
            httpMessageHandler: CreateHandler(h => h.UseProxy = false),
            connect: true,
            isProxied: _ => true);

        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Act
        var reply = await client.UnaryCall(new HelloRequest()).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual(endpoint.Address.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), reply.Message);
    }

    [Test]
    public async Task ProxiedAddress_AddressesUpdated_SubchannelDoesNotConnectSocket()
    {
        // Arrange
        var socketConnectCount = 0;
        var resolver = new TestResolver(LoggerFactory);
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("first.invalid", 80) });

        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            resolver,
            socketConnect: (socket, endPoint, cancellationToken) =>
            {
                Interlocked.Increment(ref socketConnectCount);
                return socket.ConnectAsync(endPoint, cancellationToken);
            },
            connect: true,
            isProxied: _ => true);

        var subchannel = BalancerHelpers.GetInnerLoadBalancer<PickFirstBalancer>(channel)!._subchannel!;
        Assert.AreEqual(new DnsEndPoint("first.invalid", 80), subchannel.Transport.CurrentEndPoint);

        // Act
        resolver.UpdateAddresses(new List<BalancerAddress> { new BalancerAddress("second.invalid", 80) });

        // Assert
        await TestHelpers.AssertIsTrueRetryAsync(
            () => Equals(new DnsEndPoint("second.invalid", 80), subchannel.Transport.CurrentEndPoint),
            "Wait for the subchannel to use the new address.",
            Logger).DefaultTimeout();
        await BalancerWaitHelpers.WaitForChannelStateAsync(Logger, channel, ConnectivityState.Ready).DefaultTimeout();
        Assert.AreEqual(0, socketConnectCount);
    }
}
#endif
