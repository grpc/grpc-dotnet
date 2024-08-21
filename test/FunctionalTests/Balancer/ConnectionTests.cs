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
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

namespace Grpc.AspNetCore.FunctionalTests.Balancer;

[TestFixture]
public class ConnectionTests : FunctionalTestBase
{
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
        var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));
        endpoint.Dispose();

        var connectTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            new[] { endpoint.Address },
            socketConnect: async (socket, endpoint, cancellationToken) =>
            {
                cancellationToken.Register(() => connectTcs.SetCanceled(cancellationToken));

                await connectTcs.Task;
            },
            connectTimeout: TimeSpan.FromSeconds(0.5)).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint.Method);

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => client.UnaryCall(new HelloRequest()).ResponseAsync).DefaultTimeout();
        Assert.AreEqual("A connection could not be established within the configured ConnectTimeout.", ex.Status.DebugException!.Message);

        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => connectTcs.Task).DefaultTimeout();
    }

    [Test]
    public async Task Active_UnaryCall_ConnectDispose_ConnectTaskCanceled()
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
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        // Dispose endpoint so that channel pauses while attempting to connect to the port.
        endpoint.Dispose();

        var channel = GrpcChannel.ForAddress(endpoint.Address, new GrpcChannelOptions()
        {
            LoggerFactory = LoggerFactory
        });

        Logger.LogInformation("Connecting channel.");
        var connectTask = channel.ConnectAsync();

        Logger.LogInformation("Disposing channel.");
        channel.Dispose();

        // Assert
        Logger.LogInformation("Awaiting connect task.");
        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => connectTask).DefaultTimeout();
    }

    [TestCase(0)] // TimeSpan.Zero
    [TestCase(1000)] // 1 second
    public async Task Active_UnaryCall_ConnectionIdleTimeout_SocketRecreated(int milliseconds)
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), loggerFactory: LoggerFactory);

        var connectionIdleTimeout = TimeSpan.FromMilliseconds(milliseconds);
        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            new[] { endpoint.Address },
            connectionIdleTimeout: connectionIdleTimeout).DefaultTimeout();

        Logger.LogInformation("Connecting channel.");
        await channel.ConnectAsync().DefaultTimeout();

        // Wait for timeout plus a little extra to avoid issues from imprecise timers.
        await Task.Delay(connectionIdleTimeout + TimeSpan.FromMilliseconds(50));

        var client = TestClientFactory.Create(channel, endpoint.Method);
        var response = await client.UnaryCall(new HelloRequest { Name = "Test!" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Test!", response.Message);

        AssertHasLog(LogLevel.Debug, "ClosingSocketFromIdleTimeoutOnCreateStream");
        AssertHasLog(LogLevel.Trace, "ConnectingOnCreateStream");
    }

    public async Task Active_UnaryCall_InfiniteConnectionIdleTimeout_SocketNotClosed()
    {
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), loggerFactory: LoggerFactory);

        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            new[] { endpoint.Address },
            connectionIdleTimeout: Timeout.InfiniteTimeSpan).DefaultTimeout();

        Logger.LogInformation("Connecting channel.");
        await channel.ConnectAsync();

        var client = TestClientFactory.Create(channel, endpoint.Method);
        var response = await client.UnaryCall(new HelloRequest { Name = "Test!" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Test!", response.Message);

        Assert.IsFalse(Logs.Any(l => l.EventId.Name == "ClosingSocketFromIdleTimeoutOnCreateStream"), "Shouldn't have a ClosingSocketFromIdleTimeoutOnCreateStream log.");
    }

    [Test]
    public async Task Active_UnaryCall_ServerCloseOnKeepAlive_SocketRecreatedOnRequest()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // In this test the client connects to the server, and the server then closes it after keep-alive is triggered.
        // The client then starts a gRPC call to the server. The client should discard the closed socket and create a new one.

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(
            UnaryMethod,
            nameof(UnaryMethod),
            loggerFactory: LoggerFactory,
            configureServer: o => o.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(1));

        // Don't timeout the socket or ping it from the client.
        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new RoundRobinConfig(),
            new[] { endpoint.Address },
            connectionIdleTimeout: TimeSpan.FromMinutes(30),
            socketPingInterval: TimeSpan.FromMinutes(30)).DefaultTimeout();

        Logger.LogInformation("Connecting channel.");
        await channel.ConnectAsync();

        // Fails when this test is run with debugging. Kestrel doesn't trigger keepalive timeout if debugging is enabled.
        await TestHelpers.AssertIsTrueRetryAsync(() =>
        {
            return Logs.Any(l => l.LoggerName.StartsWith("Microsoft.AspNetCore.Server.Kestrel") && l.EventId.Name == "ConnectionStop");
        }, "Wait for server to close connection.");

        var client = TestClientFactory.Create(channel, endpoint.Method);
        var response = await client.UnaryCall(new HelloRequest { Name = "Test!" }).ResponseAsync.DefaultTimeout();

        // Assert
        Assert.AreEqual("Test!", response.Message);
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
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);

        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
        {
            new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
            new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
        }));

        var socketsHttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    return true;
                }
            }
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
        SetHandlerHttpVersion(grpcWebHandler, new Version(1, 1));
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
            Assert.AreEqual(new DnsEndPoint("127.0.0.1", endpoint1.Address.Port), t.EndPoint);
        }

        // Act
        SetHandlerHttpVersion(grpcWebHandler, new Version(2, 0));
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
        Assert.AreEqual(new DnsEndPoint("127.0.0.1", endpoint1.Address.Port), activeStreams[activeStreams.Count - 1].EndPoint);

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

        SetHandlerHttpVersion(grpcWebHandler, new Version(1, 1));

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
        Assert.AreEqual($"127.0.0.1:{endpoint2.Address.Port}", host);

        activeStreams = transport.GetActiveStreams();
        Assert.AreEqual(1, activeStreams.Count);
        Assert.AreEqual(new DnsEndPoint("127.0.0.1", endpoint2.Address.Port), activeStreams[0].EndPoint);
    }

    private static void SetHandlerHttpVersion(GrpcWebHandler handler, Version version)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        handler.HttpVersion = version;
#pragma warning restore CS0618 // Type or member is obsolete
    }

#if NET7_0_OR_GREATER
    [Test]
    public async Task Active_UnaryCall_HostOverride_Success()
    {
        string? host = null;
        IPAddress? ipAddress = null;
        int? port = null;
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            host = context.Host;
            var httpContext = context.GetHttpContext();
            ipAddress = httpContext.Connection.LocalIpAddress;
            port = httpContext.Connection.LocalPort;
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Use localhost.pfx instead of server1.pfx because server1.pfx always reports RemoteCertificateNameMismatch
        // even after specifying the correct host override.
        var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
        var certPath = Path.Combine(basePath!, "localhost.pfx");
#if NET9_0_OR_GREATER
        var cert = X509CertificateLoader.LoadPkcs12FromFile(certPath, "11111");
#else
        var cert = new X509Certificate2(certPath, "11111");
#endif

        // Arrange
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true, certificate: cert);
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true, certificate: cert);

        var services = new ServiceCollection();
        services.AddSingleton((ResolverFactory)new StaticResolverFactory(_ => (new[]
        {
            CreateAddress(endpoint1.Address, "localhost"),
            CreateAddress(endpoint2.Address, "localhost")
        })));

        // Ignore that the cert isn't trusted.
        var policy = new X509ChainPolicy();
        policy.RevocationMode = X509RevocationMode.NoCheck;
        policy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

        SslPolicyErrors? callbackPolicyErrors = null;
        var socketsHttpHandler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                CertificateChainPolicy = policy,
                RemoteCertificateValidationCallback = (object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors) =>
                {
                    callbackPolicyErrors = sslPolicyErrors;
                    return true;
                }
            }
        };
        var channel = GrpcChannel.ForAddress("static:///localhost", new GrpcChannelOptions
        {
            LoggerFactory = LoggerFactory,
            HttpHandler = socketsHttpHandler,
            ServiceProvider = services.BuildServiceProvider(),
            Credentials = new SslCredentials(),
            ServiceConfig = new ServiceConfig
            {
                LoadBalancingConfigs = { new RoundRobinConfig() }
            }
        });

        await channel.ConnectAsync().DefaultTimeout();

        await BalancerWaitHelpers.WaitForSubchannelsToBeReadyAsync(Logger, channel, 2).DefaultTimeout();

        var client = TestClientFactory.Create(channel, endpoint1.Method);

        // Act
        var ports = new HashSet<int>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.UnaryCall(new HelloRequest { Name = "Balancer" }).ResponseAsync.DefaultTimeout();
            Assert.AreEqual("Balancer", response.Message);
            Assert.AreEqual("localhost", host);
            Assert.AreEqual(SslPolicyErrors.None, callbackPolicyErrors);
            Assert.AreEqual(IPAddress.Parse("127.0.0.1"), ipAddress);
            Assert.IsTrue(port == endpoint1.Address.Port || port == endpoint2.Address.Port);

            ports.Add(port!.Value);
        }

        Assert.IsTrue(ports.Contains(endpoint1.Address.Port), $"Has {endpoint1.Address.Port}");
        Assert.IsTrue(ports.Contains(endpoint2.Address.Port), $"Has {endpoint2.Address.Port}");

        static BalancerAddress CreateAddress(Uri address, string hostOverride)
        {
            var balancerAddress = new BalancerAddress(address.Host, address.Port);
            balancerAddress.Attributes.Set(ConnectionManager.HostOverrideKey, hostOverride);
            return balancerAddress;
        }
    }
#endif

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
        using var endpoint1 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);
        using var endpoint2 = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod), HttpProtocols.Http1AndHttp2, isHttps: true);

        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory>(new StaticResolverFactory(_ => new[]
        {
            new BalancerAddress(endpoint1.Address.Host, endpoint1.Address.Port),
            new BalancerAddress(endpoint2.Address.Host, endpoint2.Address.Port)
        }));
        var socketsHttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            SslOptions = new SslClientAuthenticationOptions
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

    [Test]
    public async Task SocketSendsBytes_BeforeCall_ReplaysBufferedData()
    {
        // Arrange
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(int.MaxValue);

        var acceptSocketTask = Task.Run(async () =>
        {
            var socket = await listener.AcceptAsync();
            socket.NoDelay = true;
            return socket;
        });

        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var url = $"http://localhost:{((IPEndPoint?)listener.LocalEndPoint!).Port}";

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { new Uri(url) });

        Logger.LogInformation("Client starting connect.");
        var connectTask = channel.ConnectAsync().DefaultTimeout();

        Logger.LogInformation("Server accepting socket.");
        var socket = await acceptSocketTask.DefaultTimeout();

        Logger.LogInformation("Client waiting for connect complete.");
        await connectTask.DefaultTimeout();

        var subchannel = await BalancerWaitHelpers.WaitForSubchannelToBeReadyAsync(Logger, channel).DefaultTimeout();

        // Act 1
        Logger.LogInformation("Server sending data.");
        var data = Convert.FromBase64String("AAAYBAAAAAAAAAQAP///AAUAP///AAYAACAA/gMAAAABAAAECAAAAAAAAD8AAA==");
        socket.Send(data);

        // Assert 1
        const int RequiredCheckLogCount = 2;
        await TestHelpers.AssertIsTrueRetryAsync(
            () => Logs.Count(l => l.EventId.Name == "CheckingSocket") >= RequiredCheckLogCount,
            "Wait for multiple checking socket logs.").DefaultTimeout();

        Assert.False(Logs.Any(l => l.EventId.Name == "SocketPollBadState"), "Socket was unexpectedly disconnected with a bad state.");

        // Act 2
        var cts = new CancellationTokenSource();
        var client = new Greeter.GreeterClient(channel);
        _ = client.SayHelloAsync(new HelloRequest(), new CallOptions(cancellationToken: cts.Token));

        // Assert 2
        await TestHelpers.AssertIsTrueRetryAsync(
            () => Logs.Any(l => l.EventId.Name == "StreamCreated" && HasState(l, "BufferedBytes", 46)),
            "Wait for stream to be created with buffered data.").DefaultTimeout();
        cts.Cancel();
    }

    [Test]
    public async Task SocketSendsBytes_BeforeCall_LargeData_Error()
    {
        // Arrange
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(int.MaxValue);

        var acceptSocketTask = Task.Run(async () =>
        {
            var socket = await listener.AcceptAsync();
            socket.NoDelay = true;
            return socket;
        });

        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var url = $"http://localhost:{((IPEndPoint?)listener.LocalEndPoint!).Port}";

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { new Uri(url) });

        Logger.LogInformation("Client starting connect.");
        var connectTask = channel.ConnectAsync().DefaultTimeout();

        Logger.LogInformation("Server accepting socket.");
        var socket = await acceptSocketTask.DefaultTimeout();

        Logger.LogInformation("Client waiting for connect complete.");
        await connectTask.DefaultTimeout();

        var subchannel = await BalancerWaitHelpers.WaitForSubchannelToBeReadyAsync(Logger, channel).DefaultTimeout();

        // Act
        Logger.LogInformation("Server sending data.");
        socket.Send(new byte[1024 * 1024]);

        // Assert
        const int RequiredCheckLogCount = 2;
        await TestHelpers.AssertIsTrueRetryAsync(
            () => Logs.Count(l => l.EventId.Name == "CheckingSocket") >= RequiredCheckLogCount,
            "Wait for multiple checking socket logs.").DefaultTimeout();

        Assert.True(Logs.Any(l => l.EventId.Name == "ErrorCheckingSocket" && l.Exception is { } ex && ex.Message.Contains("Maximum allowed data exceeded.")), "Socket was disconnected because of large data.");
    }

    private static bool HasState<T>(LogRecord l, string key, T expectedValue)
    {
        var values = (IReadOnlyList<KeyValuePair<string, object>>)l.State;
        var value = (T)values.FirstOrDefault(values => values.Key == key).Value;
        return Equals(expectedValue, value);
    }

    [Test]
    public async Task Connect_Idle_PingServer()
    {
        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HelloReply> UnaryMethod(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = request.Name });
        }

        // Arrange
        using var endpoint = BalancerHelpers.CreateGrpcEndpoint<HelloRequest, HelloReply>(UnaryMethod, nameof(UnaryMethod));

        var channel = await BalancerHelpers.CreateChannel(
            LoggerFactory,
            new PickFirstConfig(),
            new[] { endpoint.Address }).DefaultTimeout();

        // Act
        await channel.ConnectAsync().DefaultTimeout();

        // Assert
        const int RequiredCheckLogCount = 4;
        await TestHelpers.AssertIsTrueRetryAsync(
            () => Logs.Count(l => l.EventId.Name == "CheckingSocket") >= RequiredCheckLogCount,
            "Wait for multiple checking socket logs.").DefaultTimeout();
    }

    [Test]
    public async Task DisconnectSocket_NoCallsMade_ChannelStateUpdated()
    {
        // Arrange
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(int.MaxValue);

        var acceptSocketTask = Task.Run(async () =>
        {
            var socket = await listener.AcceptAsync();
            socket.NoDelay = true;
            return socket;
        });

        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var url = $"http://localhost:{((IPEndPoint?)listener.LocalEndPoint!).Port}";

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { new Uri(url) });

        var connectTask = channel.ConnectAsync().DefaultTimeout();

        var socket = await acceptSocketTask.DefaultTimeout();

        await connectTask.DefaultTimeout();

        var subchannel = await BalancerWaitHelpers.WaitForSubchannelToBeReadyAsync(Logger, channel).DefaultTimeout();

        var waitForConnectingTask = BalancerWaitHelpers.WaitForChannelStatesAsync(Logger, channel, new[] { ConnectivityState.Connecting }).DefaultTimeout();

        // Act
        socket.Shutdown(SocketShutdown.Both);
        socket.Dispose();
        listener.Dispose();

        // Assert
        await waitForConnectingTask.DefaultTimeout();
    }

    [Test]
    public async Task DisconnectSocket_NoCallsMade_ServerSentData_SocketClosed_ChannelStateUpdated()
    {
        using var httpEventSource = new SocketsEventSourceListener(LoggerFactory);

        // Arrange
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(int.MaxValue);

        var acceptSocketTask = Task.Run(async () =>
        {
            var socket = await listener.AcceptAsync();
            socket.NoDelay = true;
            return socket;
        });

        // Ignore errors
        SetExpectedErrorsFilter(writeContext =>
        {
            return true;
        });

        var url = $"http://localhost:{((IPEndPoint?)listener.LocalEndPoint!).Port}";

        var channel = await BalancerHelpers.CreateChannel(LoggerFactory, new RoundRobinConfig(), new[] { new Uri(url) });

        var connectTask = channel.ConnectAsync().DefaultTimeout();

        var socket = await acceptSocketTask.DefaultTimeout();
        socket.Send(Encoding.UTF8.GetBytes("Hello world"));

        await connectTask.DefaultTimeout();

        var subchannel = await BalancerWaitHelpers.WaitForSubchannelToBeReadyAsync(Logger, channel).DefaultTimeout();

        var waitForConnectingTask = BalancerWaitHelpers.WaitForChannelStatesAsync(Logger, channel, new[] { ConnectivityState.Connecting }).DefaultTimeout();

        // Act
        socket.Shutdown(SocketShutdown.Both);
        socket.Dispose();
        listener.Dispose();

        // Assert
        await waitForConnectingTask.DefaultTimeout();

        Assert.IsTrue(Logs.Any(l => l.EventId.Name == "SocketReceivingAvailable"), "Socket should have read available data.");
    }
}
#endif
