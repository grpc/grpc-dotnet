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

using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure;

public sealed record EndpointInfo(string Address, IPEndPoint EndPoint, bool isHttps);

public abstract class EndpointInfoContainerBase
{
    public abstract string Address { get; }
}

public sealed class SocketsEndpointInfoContainer(string address) : EndpointInfoContainerBase
{
    public override string Address => address;
}

public sealed class IPEndpointInfoContainer(Func<EndpointInfo> accessor) : EndpointInfoContainerBase
{
    private readonly Func<EndpointInfo> _accessor = accessor;

    public override string Address
    {
        get
        {
            var accessor = _accessor ?? throw new InvalidOperationException("WebApplication not started yet.");

            return accessor().Address;
        }
    }

    public static IPEndpointInfoContainer Create(ListenOptions listenOptions, bool isHttps)
    {
        var scheme = isHttps ? "https" : "http";
        var address = BindingAddress.Parse($"{scheme}://127.0.0.1");

        // The endpoint on listen options is updated from dynamic port placeholder (0) to a real port
        // when the server starts up. The func keeps a reference to the listen options and uses them to
        // create the address when the test requests it.
        return new IPEndpointInfoContainer(() =>
        {
            var endpoint = listenOptions.IPEndPoint!;
            var resolvedAddress = address.Scheme.ToLowerInvariant() + Uri.SchemeDelimiter + address.Host.ToLowerInvariant() + ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture);
            return new EndpointInfo(resolvedAddress, endpoint, isHttps);
        });
    }
}

public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
{
    private readonly string _socketPath = Path.GetTempFileName();
    private readonly InProcessTestServer _server;

    public GrpcTestFixture(
        Action<IServiceCollection>? initialConfigureServices = null,
        Action<WebHostBuilderContext, KestrelServerOptions, IDictionary<TestServerEndpointName, EndpointInfoContainerBase>>? configureKestrel = null,
        TestServerEndpointName? defaultClientEndpointName = null,
        Action<IConfiguration>? addConfiguration = null)
    {
        Action<IServiceCollection> configureServices = services =>
        {
            // Registers a service for tests to add new methods
            services.AddSingleton<DynamicGrpcServiceRegistry>();
        };

        _server = new InProcessTestServer<TStartup>(
            services =>
            {
                initialConfigureServices?.Invoke(services);
                configureServices(services);
            },
            (context, options, urls) =>
            {
                if (addConfiguration != null)
                {
                    addConfiguration(context.Configuration);
                }

                if (configureKestrel != null)
                {
                    configureKestrel(context, options, urls);
                    return;
                }

                options.Listen(IPAddress.Loopback, 0, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;
                    urls[TestServerEndpointName.Http2] = IPEndpointInfoContainer.Create(listenOptions, isHttps: false);
                });

                options.Listen(IPAddress.Loopback, 0, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;
                    urls[TestServerEndpointName.Http1] = IPEndpointInfoContainer.Create(listenOptions, isHttps: false);
                });

                options.Listen(IPAddress.Loopback, 0, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;

                    var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
                    var certPath = Path.Combine(basePath!, "server1.pfx");
                    listenOptions.UseHttps(certPath, "1111");

                    urls[TestServerEndpointName.Http2WithTls] = IPEndpointInfoContainer.Create(listenOptions, isHttps: true);
                });

                options.Listen(IPAddress.Loopback, 0, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http1;

                    var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
                    var certPath = Path.Combine(basePath!, "server1.pfx");
                    listenOptions.UseHttps(certPath, "1111");

                    urls[TestServerEndpointName.Http1WithTls] = IPEndpointInfoContainer.Create(listenOptions, isHttps: true);
                });

                if (File.Exists(_socketPath))
                {
                    File.Delete(_socketPath);
                }

                options.ListenUnixSocket(_socketPath, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;

                    urls[TestServerEndpointName.UnixDomainSocket] = new SocketsEndpointInfoContainer(_socketPath);
                });

                if (RequireHttp3Attribute.IsSupported(out _))
                {
                    var http3Port = Convert.ToInt32(context.Configuration["Http3Port"], CultureInfo.InvariantCulture);
                    options.Listen(IPAddress.Loopback, http3Port, listenOptions =>
                    {
#pragma warning disable CA2252 // This API requires opting into preview features
                        // Support HTTP/2 for connectivity health in load balancing to work.
                        listenOptions.Protocols = HttpProtocols.Http2 | HttpProtocols.Http3;
#pragma warning restore CA2252 // This API requires opting into preview features

                        var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
                        var certPath = Path.Combine(basePath!, "server1.pfx");
                        listenOptions.UseHttps(certPath, "1111");

                        urls[TestServerEndpointName.Http3WithTls] = IPEndpointInfoContainer.Create(listenOptions, isHttps: true);
                    });
                }
            });

        _server.StartServer();

        DynamicGrpc = _server.Host!.Services.GetRequiredService<DynamicGrpcServiceRegistry>();

        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

        (Client, Handler) = CreateHttpCore(defaultClientEndpointName);
    }

    public DynamicGrpcServiceRegistry DynamicGrpc { get; }

    public HttpMessageHandler Handler { get; }
    public HttpClient Client { get; }

    public HttpClient CreateClient(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null, Action<SocketsHttpHandler>? configureHandler = null)
    {
        return CreateHttpCore(endpointName, messageHandler, configureHandler).client;
    }

    public (HttpMessageHandler handler, Uri address) CreateHandler(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null, Action<SocketsHttpHandler>? configureHandler = null)
    {
        var result = CreateHttpCore(endpointName, messageHandler, configureHandler);
        return (result.handler, result.client.BaseAddress!);
    }

    private (HttpClient client, HttpMessageHandler handler) CreateHttpCore(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null, Action<SocketsHttpHandler>? configureHandler = null)
    {
#if HTTP3_TESTING
        endpointName ??= TestServerEndpointName.Http3WithTls;
#else
        endpointName ??= TestServerEndpointName.Http2;
#endif

        var socketsHttpHandler = new SocketsHttpHandler();
        socketsHttpHandler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (_, __, ___, ____) => true
        };

        configureHandler?.Invoke(socketsHttpHandler);

        if (endpointName == TestServerEndpointName.UnixDomainSocket)
        {
            var udsEndPoint = new UnixDomainSocketEndPoint(_server.GetUrl(endpointName.Value));
            var connectionFactory = new UnixDomainSocketConnectionFactory(udsEndPoint);

            socketsHttpHandler.ConnectCallback = connectionFactory.ConnectAsync;
        }

        HttpClient client;
        HttpMessageHandler handler;
        if (messageHandler != null)
        {
            messageHandler.InnerHandler = socketsHttpHandler;
            handler = messageHandler;
        }
        else
        {
            handler = socketsHttpHandler;
        }

        if (endpointName == TestServerEndpointName.Http3WithTls)
        {
            // TODO(JamesNK): There is a bug with SocketsHttpHandler and HTTP/3 that prevents calls
            // upgrading from 2 to 3. Force HTTP/3 calls to require that protocol.
            handler = new Http3DelegatingHandler(handler);
        }

        client = new HttpClient(handler);

        if (endpointName == TestServerEndpointName.Http2)
        {
            client.DefaultRequestVersion = new Version(2, 0);
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
        }

        client.BaseAddress = CalculateBaseAddress(endpointName.Value);

        return (client, handler);
    }

    private Uri CalculateBaseAddress(TestServerEndpointName endpointName)
    {
        if (endpointName == TestServerEndpointName.UnixDomainSocket)
        {
            return new Uri("http://localhost");
        }

        return new Uri(_server.GetUrl(endpointName));
    }

    public Uri GetUrl(TestServerEndpointName endpointName)
    {
        switch (endpointName)
        {
            case TestServerEndpointName.Http1:
            case TestServerEndpointName.Http2:
            case TestServerEndpointName.Http1WithTls:
            case TestServerEndpointName.Http2WithTls:
            case TestServerEndpointName.Http3WithTls:
                return new Uri(_server.GetUrl(endpointName));
            case TestServerEndpointName.UnixDomainSocket:
                return new Uri("http://localhost");
            default:
                throw new ArgumentException("Unexpected value: " + endpointName, nameof(endpointName));
        }
    }

    internal event Action<LogRecord> ServerLogged
    {
        add => _server.ServerLogged += value;
        remove => _server.ServerLogged -= value;
    }

    public void Dispose()
    {
        Client.Dispose();
        _server.Dispose();
        if (File.Exists(_socketPath))
        {
            File.Delete(_socketPath);
        }
    }

    private class Http3DelegatingHandler : DelegatingHandler
    {
        public Http3DelegatingHandler(HttpMessageHandler innerHandler)
        {
            InnerHandler = innerHandler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Version = new Version(3, 0);
            request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
            return base.SendAsync(request, cancellationToken);
        }
    }
}
