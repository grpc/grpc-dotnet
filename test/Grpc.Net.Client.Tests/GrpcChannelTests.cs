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

using Greet;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Tests.Infrastructure;
using Grpc.Net.Client.Web;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
#if SUPPORT_LOAD_BALANCING
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Balancer.Internal;
using Grpc.Net.Client.Tests.Infrastructure.Balancer;
#endif

namespace Grpc.Net.Client.Tests;

[TestFixture]
public class GrpcChannelTests
{
    [Test]
    public void Build_AddressWithoutHost_Error()
    {
#if SUPPORT_LOAD_BALANCING
        // Arrange & Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test.example.com:5001"))!;

        // Assert
        Assert.AreEqual("No address resolver configured for the scheme 'test.example.com'.", ex.Message);
#else
        // Arrange & Act
        var ex = Assert.Throws<ArgumentException>(() => GrpcChannel.ForAddress("test.example.com:5001"))!;

        // Assert
        Assert.AreEqual("Address 'test.example.com:5001' doesn't have a host. Address should include a scheme, host, and optional port. For example, 'https://localhost:5001'.", ex.Message);
#endif
    }

    [TestCase("https://localhost:5001/path", true)]
    [TestCase("https://localhost:5001/?query=ya", true)]
    [TestCase("https://localhost:5001//", true)]
    [TestCase("https://localhost:5001/", false)]
    [TestCase("https://localhost:5001", false)]
    public void Build_AddressWithPath_Log(string address, bool hasPathOrQuery)
    {
        // Arrange
        var testSink = new TestSink();
        var testFactory = new TestLoggerFactory(testSink, enabled: true);

        // Act
        GrpcChannel.ForAddress(address, CreateGrpcChannelOptions(o => o.LoggerFactory = testFactory));

        // Assert
        var log = testSink.Writes.SingleOrDefault(w => w.EventId.Name == "AddressPathUnused");
        if (hasPathOrQuery)
        {
            Assert.IsNotNull(log);
            Assert.AreEqual(LogLevel.Debug, log!.LogLevel);

            var message = $"The path in the channel's address '{address}' won't be used when making gRPC calls. " +
                "A DelegatingHandler can be used to include a path with gRPC calls. See https://aka.ms/aspnet/grpc/subdir for details.";
            Assert.AreEqual(message, log.Message);
        }
        else
        {
            Assert.IsNull(log);
        }
    }

    [Test]
    public void Build_SslCredentialsWithHttps_Success()
    {
        // Arrange & Act
        var channel = GrpcChannel.ForAddress("https://localhost",
            CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials()));

        // Assert
        Assert.IsTrue(channel.IsSecure);
    }

    [TestCase("http://localhost")]
    [TestCase("https://localhost")]
    public void DebuggerToString_HttpAddress_ExpectedResult(string address)
    {
        // Arrange
        var channel = GrpcChannel.ForAddress(address,
            CreateGrpcChannelOptions());

        // Act & Assert
        Assert.AreEqual($@"Address = ""{address}""", channel.DebuggerToString());
    }

    [Test]
    public void DebuggerToString_Dispose_ExpectedResult()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("http://localhost",
            CreateGrpcChannelOptions());
        channel.Dispose();

        // Act & Assert
        Assert.AreEqual(@"Address = ""http://localhost"", Disposed = true", channel.DebuggerToString());
    }

#if SUPPORT_LOAD_BALANCING
    [Test]
    public void DebuggerToString_NonHttpAddress_ExpectedResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler,
            Credentials = ChannelCredentials.SecureSsl
        };
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);

        // Act
        var debugText = channel.DebuggerToString();

        // Assert
        Assert.AreEqual(@"Address = ""test:///localhost"", IsSecure = true", debugText);
    }
#endif

    [TestCase("http://localhost")]
    [TestCase("HTTP://localhost")]
    public void Build_SslCredentialsWithHttp_ThrowsError(string address)
    {
        // Arrange & Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress(address,
            CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials())))!;

        // Assert
        Assert.AreEqual("Channel is configured with secure channel credentials and can't use a HttpClient with a 'http' scheme.", ex.Message);
    }

    [TestCase("https://localhost")]
    [TestCase("HTTPS://localhost")]
    public void Build_SslCredentialsWithArgs_ThrowsError(string address)
    {
        // Arrange & Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress(address,
            CreateGrpcChannelOptions(o => o.Credentials = new SslCredentials("rootCertificates!!!"))))!;

        // Assert
        Assert.AreEqual(
            "SslCredentials with non-null arguments is not supported by GrpcChannel. " +
            "GrpcChannel uses HttpClient to make gRPC calls and HttpClient automatically loads root certificates from the operating system certificate store. " +
            "Client certificates should be configured on HttpClient. See https://aka.ms/aspnet/grpc/certauth for details.", ex.Message);
    }

    [Test]
    public void Build_InsecureCredentialsWithHttp_Success()
    {
        // Arrange & Act
        var channel = GrpcChannel.ForAddress("http://localhost",
            CreateGrpcChannelOptions(o => o.Credentials = ChannelCredentials.Insecure));

        // Assert
        Assert.IsFalse(channel.IsSecure);
    }

    private static GrpcChannelOptions CreateGrpcChannelOptions(Action<GrpcChannelOptions>? func = null)
    {
        var o = new GrpcChannelOptions();
#if NET462
        // An error is thrown if no handler is specified by .NET Standard 2.0 target.
        o.HttpHandler = new NullHttpHandler();
#endif
        func?.Invoke(o);
        return o;
    }

    [Test]
    public void Build_InsecureCredentialsWithHttps_ThrowsError()
    {
        // Arrange & Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost",
            CreateGrpcChannelOptions(o => o.Credentials = ChannelCredentials.Insecure)))!;

        // Assert
        Assert.AreEqual("Channel is configured with insecure channel credentials and can't use a HttpClient with a 'https' scheme.", ex.Message);
    }

#if SUPPORT_LOAD_BALANCING
    [Test]
    public void Build_ConnectTimeout_ReadFromSocketsHttpHandler()
    {
        // Arrange & Act
        var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions(o => o.HttpHandler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(1)
        }));

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(1), channel.ConnectTimeout);
    }

    [TestCase(-1, -1, -1)]
    [TestCase(0, 0, 0)]
    [TestCase(0, -1, 0)]
    [TestCase(-1, 0, 0)]
    [TestCase(1000, -1, 1000)]
    [TestCase(-1, 1000, 1000)]
    [TestCase(500, 1000, 1000)]
    [TestCase(1000, 500, 1000)]
    public void Build_ConnectionIdleTimeout_ReadFromSocketsHttpHandler(
        int? pooledConnectionIdleTimeoutMs,
        int? pooledConnectionLifetimeMs,
        int expectedConnectionIdleTimeoutMs)
    {
        // Arrange
        var handler = new SocketsHttpHandler();
        if (pooledConnectionIdleTimeoutMs != null)
        {
            handler.PooledConnectionIdleTimeout = TimeSpan.FromMilliseconds(pooledConnectionIdleTimeoutMs.Value);
        }
        if (pooledConnectionLifetimeMs != null)
        {
            handler.PooledConnectionLifetime = TimeSpan.FromMilliseconds(pooledConnectionLifetimeMs.Value);
        }

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions(o => o.HttpHandler = handler));

        // Assert
        Assert.AreEqual(TimeSpan.FromMilliseconds(expectedConnectionIdleTimeoutMs), channel.ConnectionIdleTimeout);
    }
#endif

    [Test]
    public void Build_HttpClientAndHttpHandler_ThrowsError()
    {
        // Arrange & Act
        var ex = Assert.Throws<ArgumentException>(() => GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpClient = new HttpClient(),
            HttpHandler = new HttpClientHandler()
        }))!;

        // Assert
        Assert.AreEqual("HttpClient and HttpHandler have been configured. Only one HTTP caller can be specified.", ex.Message);
    }

    [Test]
    public async Task Build_HttpClient_UsedForRequestsAsync()
    {
        // Arrange
        var handler = new ExceptionHttpMessageHandler("HttpClient");
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpClient = new HttpClient(handler)
        });
        var client = new Greeter.GreeterClient(channel);

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await client.SayHelloAsync(new HelloRequest()));

        // Assert
        Assert.AreEqual("HttpClient", ex.Status.DebugException!.Message);
    }

    [Test]
    public async Task Build_HttpHandler_UsedForRequestsAsync()
    {
        // Arrange
        var handler = new ExceptionHttpMessageHandler("HttpHandler");
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
        var client = new Greeter.GreeterClient(channel);

        // Act
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(async () => await client.SayHelloAsync(new HelloRequest())).DefaultTimeout();

        // Assert
        Assert.AreEqual("HttpHandler", ex.Status.DebugException!.Message);
    }

    [Test]
    public void Build_ForAddressNoOptions_ValidChannel()
    {
        // Arrange & Act
        using var channel = GrpcChannel.ForAddress("https://localhost");

        // Assert
        Assert.NotNull(channel);
#if NET462
        Assert.AreEqual(HttpHandlerType.WinHttpHandler, channel.HttpHandlerType);
#else
        Assert.AreEqual(HttpHandlerType.SocketsHttpHandler, channel.HttpHandlerType);
#endif
    }

    [Test]
    public void Build_ServiceConfigDuplicateMethodConfigNames_Error()
    {
        // Arrange
        var options = CreateGrpcChannelOptions(o => o.ServiceConfig = new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig { Names = { MethodName.Default } },
                new MethodConfig { Names = { MethodName.Default } }
            }
        });

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("https://localhost", options))!;

        // Assert
        Assert.AreEqual("Duplicate method config found. Service: '', method: ''.", ex.Message);
    }

    [Test]
    public void Dispose_NotCalled_NotDisposed()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

        // Act (nothing)

        // Assert
        Assert.IsFalse(channel.Disposed);
    }

#if !NET462
    [Test]
    public void Dispose_Called_Disposed()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost");

        // Act
        channel.Dispose();

        // Assert
        Assert.IsTrue(channel.Disposed);
        Assert.Throws<ObjectDisposedException>(() => channel.HttpInvoker.SendAsync(new HttpRequestMessage(), CancellationToken.None));
    }
#endif

    [Test]
    public void Dispose_CalledMultipleTimes_Disposed()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

        // Act
        channel.Dispose();
        channel.Dispose();

        // Assert
        Assert.IsTrue(channel.Disposed);
    }

    [Test]
    public void Dispose_CreateCallInvoker_ThrowError()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());

        // Act
        channel.Dispose();

        // Assert
        Assert.Throws<ObjectDisposedException>(() => channel.CreateCallInvoker());
    }

    [Test]
    public async Task Dispose_StartCallOnClient_ThrowError()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost", CreateGrpcChannelOptions());
        var client = new Greet.Greeter.GreeterClient(channel);

        // Act
        channel.Dispose();

        // Assert
        await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => client.SayHelloAsync(new Greet.HelloRequest()).ResponseAsync);
    }

    [Test]
    public void Dispose_CalledWhenHttpClientSpecified_HttpClientNotDisposed()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        // Act
        channel.Dispose();

        // Assert
        Assert.IsTrue(channel.Disposed);
        Assert.IsFalse(handler.Disposed);
    }

    [Test]
    public void Dispose_CalledWhenHttpClientSpecifiedAndHttpClientDisposedTrue_HttpClientDisposed()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var httpClient = new HttpClient(handler);
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpClient = httpClient,
            DisposeHttpClient = true
        });

        // Act
        channel.Dispose();

        // Assert
        Assert.IsTrue(channel.Disposed);
        Assert.IsTrue(handler.Disposed);
    }

    [Test]
    public void Dispose_CalledWhenHttpMessageHandlerSpecifiedAndHttpClientDisposedTrue_HttpClientDisposed()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler,
            DisposeHttpClient = true
        });

        // Act
        channel.Dispose();

        // Assert
        Assert.IsTrue(channel.Disposed);
        Assert.IsTrue(handler.Disposed);
    }

    [Test]
    public async Task Dispose_CalledWhileActiveCalls_ActiveCallsDisposed()
    {
        // Arrange
        var handler = new TestHttpMessageHandler();
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler
        });

        var client = new Greeter.GreeterClient(channel);
        var call = client.SayHelloAsync(new HelloRequest());

        var exTask = ExceptionAssert.ThrowsAsync<RpcException>(() => call.ResponseAsync);
        Assert.IsFalse(exTask.IsCompleted);
        Assert.AreEqual(1, channel.GetActiveCalls().Length);

        // Act
        channel.Dispose();

        // Assert
        var ex = await exTask.DefaultTimeout();
        Assert.AreEqual(StatusCode.Cancelled, ex.StatusCode);
        Assert.AreEqual("gRPC call disposed.", ex.Status.Detail);

        Assert.IsTrue(channel.Disposed);
        Assert.AreEqual(0, channel.GetActiveCalls().Length);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HttpHandler_HttpClientHandlerOverNativeOnAndroid_ThrowError(bool useDelegatingHandlers)
    {
        // Arrange
        AppContext.SetSwitch("System.Net.Http.UseNativeHttpHandler", true);
        
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IOperatingSystem>(new TestOperatingSystem { IsAndroid = true });

            HttpMessageHandler handler = new HttpClientHandler();

            // Add an extra handler to verify that test successfully recurses down custom handlers.
            if (useDelegatingHandlers)
            {
                handler = new TestDelegatingHandler(handler);
            }

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
                {
                    HttpHandler = handler,
                    ServiceProvider = services.BuildServiceProvider()
                });
            });

            Assert.AreEqual(ex!.Message, "The channel configuration isn't valid on Android devices. " +
                "The channel is configured to use HttpClientHandler and Android's native HTTP/2 library. " +
                "gRPC isn't fully supported by Android's native HTTP/2 library and it can cause runtime errors. " +
                "To fix this problem, either configure the channel to use SocketsHttpHandler, or add " +
                "<UseNativeHttpHandler>false</UseNativeHttpHandler> to the app's project file. " +
                "For more information, see https://aka.ms/aspnet/grpc/android.");
        }
        finally
        {
            // Reset switch for other tests.
            AppContext.SetSwitch("System.Net.Http.UseNativeHttpHandler", false);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HttpHandler_HttpClientHandlerOverNativeOnAndroid_HasGrpcWebHandler_ThrowError(bool useDelegatingHandlers)
    {
        // Arrange
        AppContext.SetSwitch("System.Net.Http.UseNativeHttpHandler", true);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<IOperatingSystem>(new TestOperatingSystem { IsAndroid = true });

            HttpMessageHandler handler = new HttpClientHandler();
            handler = new GrpcWebHandler(handler);

            // Add an extra handler to verify that test successfully recurses down custom handlers.
            if (useDelegatingHandlers)
            {
                handler = new TestDelegatingHandler(handler);
            }

            var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = handler,
                ServiceProvider = services.BuildServiceProvider()
            });

            // Assert
            Assert.IsTrue(channel.OperatingSystem.IsAndroid);
        }
        finally
        {
            // Reset switch for other tests.
            AppContext.SetSwitch("System.Net.Http.UseNativeHttpHandler", false);
        }
    }

    private class TestDelegatingHandler : DelegatingHandler
    {
        public TestDelegatingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }
    }

    [Test]
    [TestCase(null)]
    [TestCase(false)]
    public void HttpHandler_HttpClientHandlerOverSocketsOnAndroid_Success(bool? isNativeHttpHandler)
    {
        // Arrange
        if (isNativeHttpHandler != null)
        {
            AppContext.SetSwitch("System.Net.Http.UseNativeHttpHandler", isNativeHttpHandler.Value);
        }

        var services = new ServiceCollection();
        services.AddSingleton<IOperatingSystem>(new TestOperatingSystem { IsAndroid = true });

        var handler = new HttpClientHandler();

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler,
            ServiceProvider = services.BuildServiceProvider()
        });

        // Assert
        Assert.IsTrue(channel.OperatingSystem.IsAndroid);
    }

    private class TestOperatingSystem : IOperatingSystem
    {
        public bool IsBrowser { get; set; }
        public bool IsAndroid { get; set; }
        public bool IsWindows { get; set; }
        public bool IsWindowsServer { get; }
        public Version OSVersion { get; set; } = new Version(1, 2, 3, 4);
    }

    [Test]
    public void WinHttpHandler_UnsupportedWindows_Throw()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperatingSystem>(new TestOperatingSystem
        {
            IsWindows = true,
            OSVersion = new Version(1, 2, 3, 4)
        });

#pragma warning disable CS0436 // Just need to have a type called WinHttpHandler to activate new behavior.
        var winHttpHandler = new WinHttpHandler(new TestHttpMessageHandler());
#pragma warning restore CS0436

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
            {
                HttpHandler = winHttpHandler,
                ServiceProvider = services.BuildServiceProvider()
            });
        });

        // Assert
        Assert.AreEqual(ex!.Message, "The channel configuration isn't valid on this operating system. " +
                "The channel is configured to use WinHttpHandler and the current version of Windows " +
                "doesn't support HTTP/2 features required by gRPC. Windows Server 2022 or Windows 11 or later is required. " +
                "For more information, see https://aka.ms/aspnet/grpc/netframework.");
    }

#pragma warning disable CS0436 // Just need to have a type called WinHttpHandler to activate new behavior.
    [TestCase(typeof(WinHttpHandler))]
#pragma warning restore CS0436
    [TestCase(typeof(WinHttpHandlerInherited))]
    public void WinHttpHandler_SupportedWindows_Success(Type handlerType)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IOperatingSystem>(new TestOperatingSystem
        {
            IsWindows = true,
            OSVersion = Version.Parse("10.0.20348.169")
        });

        var winHttpHandler = (HttpMessageHandler)Activator.CreateInstance(handlerType, new TestHttpMessageHandler())!;

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", new GrpcChannelOptions
        {
            HttpHandler = winHttpHandler,
            ServiceProvider = services.BuildServiceProvider()
        });

        // Assert
        Assert.AreEqual(HttpHandlerType.WinHttpHandler, channel.HttpHandlerType);
    }

#pragma warning disable CS0436 // Just need to have a type called WinHttpHandler to activate new behavior.
    private class WinHttpHandlerInherited : WinHttpHandler
    {
        public WinHttpHandlerInherited(HttpMessageHandler innerHandler) : base(innerHandler)
        {
        }
    }
#pragma warning restore CS0436

#if SUPPORT_LOAD_BALANCING
    [Test]
    public void Resolver_SocketHttpHandlerWithConnectCallback_Error()
    {
        ConfigureLoadBalancingWithInvalidHttpHandler(o =>
        {
            o.HttpHandler = new SocketsHttpHandler
            {
                ConnectCallback = (context, ct) => new ValueTask<Stream>(new MemoryStream())
            };
        });
    }

    [Test]
    public void Resolver_HttpClientHandler_Error()
    {
        ConfigureLoadBalancingWithInvalidHttpHandler(o =>
        {
            o.HttpHandler = new HttpClientHandler();
        });
    }

    [Test]
    public void Resolver_HttpClient_Error()
    {
        ConfigureLoadBalancingWithInvalidHttpHandler(o =>
        {
            o.HttpClient = new HttpClient();
        });
    }

    private static void ConfigureLoadBalancingWithInvalidHttpHandler(Action<GrpcChannelOptions> channelOptionsFunc)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();

        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            Credentials = ChannelCredentials.Insecure
        };
        channelOptionsFunc(channelOptions);

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost", channelOptions))!;

        // Assert
        Assert.AreEqual("Channel is configured with an HTTP transport doesn't support client-side load balancing or connectivity state tracking. " +
            "The underlying HTTP transport must be a SocketsHttpHandler with no SocketsHttpHandler.ConnectCallback configured. " +
            "The HTTP transport must be configured on the channel using GrpcChannelOptions.HttpHandler.", ex.Message);
    }

    [Test]
    public void Resolver_NoChannelCredentials_Error()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost", channelOptions))!;

        // Assert
        Assert.AreEqual("Unable to determine the TLS configuration of the channel from address 'test:///localhost'. " +
            "GrpcChannelOptions.Credentials must be specified when the address doesn't have a 'http' or 'https' scheme. " +
            "To call TLS endpoints, set credentials to 'ChannelCredentials.SecureSsl'. " +
            "To call non-TLS endpoints, set credentials to 'ChannelCredentials.Insecure'.", ex.Message);
    }

    [Test]
    public async Task ConnectAsync_AfterDispose_DisposedError()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost");
        channel.Dispose();

        // Act & Assert
        await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => channel.ConnectAsync());
    }

    [Test]
    public async Task State_ConnectAndDispose_StateChanges()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        // Assert
        Assert.AreEqual(ConnectivityState.Idle, channel.State);

        await channel.ConnectAsync().DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, channel.State);

        channel.Dispose();
        Assert.AreEqual(ConnectivityState.Shutdown, channel.State);
    }

    [Test]
    public async Task WaitForStateChangedAsync_AfterDispose_DisposedError()
    {
        // Arrange
        var channel = GrpcChannel.ForAddress("https://localhost");
        channel.Dispose();

        // Act & Assert
        await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => channel.WaitForStateChangedAsync(ConnectivityState.Idle));
    }

    [Test]
    public async Task WaitForStateChangedAsync_ConnectAndDispose_StateChanges()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        var waitForNonIdleTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);

        // Assert
        Assert.AreEqual(ConnectivityState.Idle, channel.State);
        Assert.IsFalse(waitForNonIdleTask.IsCompleted);

        await channel.ConnectAsync().DefaultTimeout();
        await waitForNonIdleTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Ready, channel.State);

        var waitForNonReadyTask = channel.WaitForStateChangedAsync(ConnectivityState.Ready);
        Assert.IsFalse(waitForNonReadyTask.IsCompleted);

        channel.Dispose();
        await waitForNonReadyTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Shutdown, channel.State);

        await ExceptionAssert.ThrowsAsync<ObjectDisposedException>(() => channel.WaitForStateChangedAsync(ConnectivityState.Ready)).DefaultTimeout();
    }

    [Test]
    public async Task WaitForStateChangedAsync_CancellationTokenBeforeEvent_StateChanges()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };
        var cts = new CancellationTokenSource();

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        var waitForNonIdleTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);
        var waitForNonIdleWithCancellationTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);
        var waitForNonIdleWithCancellationDupeTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);

        // Assert
        Assert.IsFalse(waitForNonIdleTask.IsCompleted);
        Assert.IsFalse(waitForNonIdleWithCancellationTask.IsCompleted);
        Assert.IsFalse(waitForNonIdleWithCancellationDupeTask.IsCompleted);

        cts.Cancel();
        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => waitForNonIdleWithCancellationTask).DefaultTimeout();
        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => waitForNonIdleWithCancellationDupeTask).DefaultTimeout();

        await channel.ConnectAsync().DefaultTimeout();
        await waitForNonIdleTask.DefaultTimeout();
    }

    [Test]
    public async Task WaitForStateChangedAsync_CancellationTokenAfterEvent_StateChanges()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };
        var cts = new CancellationTokenSource();

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        var waitForNonIdleTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle);
        var waitForNonIdleWithCancellationTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);
        var waitForNonIdleWithCancellationDupeTask = channel.WaitForStateChangedAsync(ConnectivityState.Idle, cts.Token);

        // Assert
        Assert.IsFalse(waitForNonIdleTask.IsCompleted);
        Assert.IsFalse(waitForNonIdleWithCancellationTask.IsCompleted);
        Assert.IsFalse(waitForNonIdleWithCancellationDupeTask.IsCompleted);

        await channel.ConnectAsync().DefaultTimeout();
        await waitForNonIdleTask.DefaultTimeout();
        await waitForNonIdleWithCancellationTask.DefaultTimeout();
        await waitForNonIdleWithCancellationDupeTask.DefaultTimeout();

        cts.Cancel();
    }

    [Test]
    public async Task ConnectAsync_ShiftThroughStates_CompleteOnReady()
    {
        // Arrange
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var currentConnectivityState = ConnectivityState.TransientFailure;

        var services = new ServiceCollection();
        services.AddNUnitLogger();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory>(TestSubchannelTransportFactory.Create(async (s, c) =>
        {
            await syncPoint.WaitToContinue();
            return new TryConnectResult(currentConnectivityState);
        }));

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        var waitForStateTask = WaitForStateAsync(channel, ConnectivityState.Connecting);

        // Assert
        Assert.IsFalse(waitForStateTask.IsCompleted);

        var connectTask = channel.ConnectAsync();
        Assert.IsFalse(connectTask.IsCompleted);

        await waitForStateTask.DefaultTimeout();

        waitForStateTask = WaitForStateAsync(channel, ConnectivityState.TransientFailure);

        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        syncPoint.Continue();
        syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        await waitForStateTask.DefaultTimeout();

        waitForStateTask = WaitForStateAsync(channel, ConnectivityState.Ready);
        Assert.IsFalse(connectTask.IsCompleted);

        await syncPoint.WaitForSyncPoint().DefaultTimeout();
        currentConnectivityState = ConnectivityState.Ready;
        syncPoint.Continue();
        syncPoint = new SyncPoint(runContinuationsAsynchronously: true);

        await connectTask.DefaultTimeout();
        await waitForStateTask.DefaultTimeout();

        Assert.AreEqual(ConnectivityState.Ready, channel.State);

        waitForStateTask = WaitForStateAsync(channel, ConnectivityState.Ready);
        channel.Dispose();

        await waitForStateTask.DefaultTimeout();
        Assert.AreEqual(ConnectivityState.Shutdown, channel.State);
    }

    [Test]
    public async Task ConnectAsync_DisposeDuringConnect_ConnectTaskCanceled()
    {
        // Arrange
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var currentConnectivityState = ConnectivityState.TransientFailure;

        var services = new ServiceCollection();
        services.AddNUnitLogger();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory>(TestSubchannelTransportFactory.Create(async (s, c) =>
        {
            await syncPoint.WaitToContinue();
            return new TryConnectResult(currentConnectivityState);
        }));

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        var connectTask = channel.ConnectAsync();

        // Assert
        Assert.IsFalse(connectTask.IsCompleted);

        channel.Dispose();

        await ExceptionAssert.ThrowsAsync<OperationCanceledException>(() => connectTask).DefaultTimeout();
    }

    private static async Task WaitForStateAsync(GrpcChannel channel, ConnectivityState state)
    {
        while (true)
        {
            var currentState = channel.State;
            
            if (currentState == state)
            {
                return;
            }

            await channel.WaitForStateChangedAsync(currentState);
        }
    }

    [Test]
    public async Task ConnectAsync_ConnectivityNotSupported_Error()
    {
        await ConnectivityActionOnChannelWhenConnectivityNotSupported(channel => Task.FromResult(channel.ConnectAsync()));
    }

    [Test]
    public async Task State_ConnectivityNotSupported_Error()
    {
        await ConnectivityActionOnChannelWhenConnectivityNotSupported(channel =>
        {
            Console.WriteLine(channel.State);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task WaitForStateChangedAsync_ConnectivityNotSupported_Error()
    {
        await ConnectivityActionOnChannelWhenConnectivityNotSupported(channel => channel.WaitForStateChangedAsync(channel.State));
    }

    private static async Task ConnectivityActionOnChannelWhenConnectivityNotSupported(Func<GrpcChannel, Task> action)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();

        var channelOptions = new GrpcChannelOptions
        {
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = new TestHttpMessageHandler()
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);
        var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => action(channel)).DefaultTimeout();

        // Assert
        Assert.AreEqual("Channel is configured with an HTTP transport doesn't support client-side load balancing or connectivity state tracking. " +
            "The underlying HTTP transport must be a SocketsHttpHandler with no SocketsHttpHandler.ConnectCallback configured. " +
            "The HTTP transport must be configured on the channel using GrpcChannelOptions.HttpHandler.", ex.Message);
    }

    [Test]
    public void Resolver_MatchInServiceProvider_Success()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);

        // Assert
        Assert.IsInstanceOf(typeof(ChannelTestResolver), channel.ConnectionManager._resolver);
    }

    [Test]
    public void Resolver_NoMatchInServiceProvider_Error()
    {
        // Arrange
        var services = new ServiceCollection();

        var channelOptions = new GrpcChannelOptions
        {
            Credentials = ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider()
        };

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost", channelOptions))!;

        // Assert
        Assert.AreEqual("No address resolver configured for the scheme 'test'.", ex.Message);
    }

    [Test]
    public void Resolver_NoServiceProvider_Error()
    {
        // Arrange & Act
        var ex = Assert.Throws<InvalidOperationException>(() => GrpcChannel.ForAddress("test:///localhost"))!;

        // Assert
        Assert.AreEqual("No address resolver configured for the scheme 'test'.", ex.Message);
    }

    [TestCase(false, 80)]
    [TestCase(true, 443)]
    public void Resolver_DefaultPort_MatchesSecure(bool isSecure, int expectedPort)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ResolverFactory, ChannelTestResolverFactory>();
        services.AddSingleton<ISubchannelTransportFactory, TestSubchannelTransportFactory>();

        var handler = new TestHttpMessageHandler();
        var channelOptions = new GrpcChannelOptions
        {
            Credentials = isSecure ? ChannelCredentials.SecureSsl : ChannelCredentials.Insecure,
            ServiceProvider = services.BuildServiceProvider(),
            HttpHandler = handler
        };

        // Act
        var channel = GrpcChannel.ForAddress("test:///localhost", channelOptions);

        // Assert
        Assert.IsInstanceOf(typeof(ChannelTestResolver), channel.ConnectionManager._resolver);

        var resolver = (ChannelTestResolver)channel.ConnectionManager._resolver;
        Assert.AreEqual(expectedPort, resolver.Options.DefaultPort);
    }

    public class ChannelTestResolverFactory : ResolverFactory
    {
        public override string Name => "test";

        public override Resolver Create(ResolverOptions options)
        {
            return new ChannelTestResolver(options);
        }
    }

    public class ChannelTestResolver : Resolver
    {
        public ChannelTestResolver(ResolverOptions options)
        {
            Options = options;
        }

        public ResolverOptions Options { get; }

        public override void Start(Action<ResolverResult> listener)
        {
            throw new NotImplementedException();
        }
    }

    [Test]
    public void InitialReconnectBackoff_FirstBackOff_MatchConfiguredBackoff()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IRandomGenerator, TestRandomGenerator>();

        var channelOptions = new GrpcChannelOptions
        {
            InitialReconnectBackoff = TimeSpan.FromSeconds(0.2),
            ServiceProvider = services.BuildServiceProvider()
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);
        var backoffPolicy = channel.ConnectionManager.BackoffPolicyFactory.Create();

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(0.2), backoffPolicy.NextBackoff());
    }

    [Test]
    public void MaxReconnectBackoff_ManyBackoffs_MatchConfiguredBackoff()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<IRandomGenerator, TestRandomGenerator>();

        var channelOptions = new GrpcChannelOptions
        {
            MaxReconnectBackoff = TimeSpan.FromSeconds(10),
            ServiceProvider = services.BuildServiceProvider()
        };

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);
        var backoffPolicy = channel.ConnectionManager.BackoffPolicyFactory.Create();

        // Assert
        for (var i = 0; i < 100; i++)
        {
            if (backoffPolicy.NextBackoff() == TimeSpan.FromMinutes(10))
            {
                break;
            }
        }

        Assert.AreEqual(TimeSpan.FromSeconds(10), backoffPolicy.NextBackoff());
    }

    [Test]
    public void MaxReconnectBackoff_Get_IsExpectedDefault()
    {
        // Arrange
        var channelOptions = new GrpcChannelOptions();

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(120), channelOptions.MaxReconnectBackoff);
        Assert.AreEqual(TimeSpan.FromSeconds(120), channel.MaxReconnectBackoff);
    }

    [Test]
    public void InitialReconnectBackoff_Get_IsExpectedDefault()
    {
        // Arrange
        var channelOptions = new GrpcChannelOptions();

        // Act
        var channel = GrpcChannel.ForAddress("https://localhost", channelOptions);

        // Assert
        Assert.AreEqual(TimeSpan.FromSeconds(1), channelOptions.InitialReconnectBackoff);
        Assert.AreEqual(TimeSpan.FromSeconds(1), channel.InitialReconnectBackoff);
    }
#endif

    public class TestRandomGenerator : IRandomGenerator
    {
        public int Next(int minValue, int maxValue)
        {
            return 0;
        }

        public double NextDouble()
        {
            return 0.5;
        }
    }

    public class TestHttpMessageHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<HttpResponseMessage>();
            cancellationToken.Register(s => ((TaskCompletionSource<HttpResponseMessage>)s!).SetException(new OperationCanceledException()), tcs);
            return await tcs.Task;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
        }
    }

    public class ExceptionHttpMessageHandler : HttpMessageHandler
    {
        public string ExceptionMessage { get; }

        public ExceptionHttpMessageHandler(string exceptionMessage)
        {
            ExceptionMessage = exceptionMessage;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(new InvalidOperationException(ExceptionMessage));
        }
    }
}
