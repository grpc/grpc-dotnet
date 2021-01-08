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

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
    {
        private readonly InProcessTestServer _server;

        public GrpcTestFixture(
            Action<IServiceCollection>? initialConfigureServices = null,
            Action<KestrelServerOptions, IDictionary<TestServerEndpointName, string>>? configureKestrel = null,
            TestServerEndpointName? defaultClientEndpointName = null)
        {
            LoggerFactory = new LoggerFactory();

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
                (options, urls) =>
                {
                    if (configureKestrel != null)
                    {
                        configureKestrel(options, urls);
                        return;
                    }

                    urls[TestServerEndpointName.Http2] = "http://127.0.0.1:50050";
                    options.ListenLocalhost(50050, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });

                    urls[TestServerEndpointName.Http1] = "http://127.0.0.1:50040";
                    options.ListenLocalhost(50040, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;
                    });

                    urls[TestServerEndpointName.Http2WithTls] = "https://127.0.0.1:50030";
                    options.ListenLocalhost(50030, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;

                        var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
                        var certPath = Path.Combine(basePath!, "server1.pfx");
                        listenOptions.UseHttps(certPath, "1111");
                    });

                    urls[TestServerEndpointName.Http1WithTls] = "https://127.0.0.1:50020";
                    options.ListenLocalhost(50020, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http1;

                        var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
                        var certPath = Path.Combine(basePath!, "server1.pfx");
                        listenOptions.UseHttps(certPath, "1111");
                    });

#if NET6_0_OR_GREATER
                    if (RequireHttp3Attribute.IsSupported(out _))
                    {
                        urls[TestServerEndpointName.Http3WithTls] = "https://127.0.0.1:50019";
                        options.ListenLocalhost(50019, listenOptions =>
                        {
                        // Support HTTP/2 for connectivity health in load balancing to work.
                        listenOptions.Protocols = HttpProtocols.Http2 | HttpProtocols.Http3;

                            var basePath = Path.GetDirectoryName(typeof(InProcessTestServer).Assembly.Location);
                            var certPath = Path.Combine(basePath!, "server1.pfx");
                            listenOptions.UseHttps(certPath, "1111");
                        });
                    }
#endif
                });

            _server.StartServer();

            DynamicGrpc = _server.Host!.Services.GetRequiredService<DynamicGrpcServiceRegistry>();

#if !NET5_0
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
#endif

            (Client, Handler) = CreateHttpCore(defaultClientEndpointName);
        }

        public ILoggerFactory LoggerFactory { get; }
        public DynamicGrpcServiceRegistry DynamicGrpc { get; }

        public HttpMessageHandler Handler { get; }
        public HttpClient Client { get; }

        public HttpClient CreateClient(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null)
        {
            return CreateHttpCore(endpointName, messageHandler).client;
        }

        public (HttpMessageHandler handler, Uri address) CreateHandler(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null)
        {
            var result = CreateHttpCore(endpointName, messageHandler);
            return (result.handler, result.client.BaseAddress!);
        }

        private (HttpClient client, HttpMessageHandler handler) CreateHttpCore(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null)
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

#if NET6_0_OR_GREATER
            if (endpointName == TestServerEndpointName.Http3WithTls)
            {
                // TODO(JamesNK): There is a bug with SocketsHttpHandler and HTTP/3 that prevents calls
                // upgrading from 2 to 3. Force HTTP/3 calls to require that protocol.
                handler = new Http3DelegatingHandler(handler);
            }
#endif

            client = new HttpClient(handler);

            if (endpointName == TestServerEndpointName.Http2)
            {
                client.DefaultRequestVersion = new Version(2, 0);
#if NET5_0_OR_GREATER
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
#endif
            }
            client.BaseAddress = new Uri(_server.GetUrl(endpointName.Value));

            return (client, handler);
        }

        public Uri GetUrl(TestServerEndpointName endpointName)
        {
            switch (endpointName)
            {
                case TestServerEndpointName.Http1:
                case TestServerEndpointName.Http2:
                case TestServerEndpointName.Http1WithTls:
                case TestServerEndpointName.Http2WithTls:
#if NET6_0_OR_GREATER
                case TestServerEndpointName.Http3WithTls:
#endif
                    return new Uri(_server.GetUrl(endpointName));
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
        }

#if NET6_0_OR_GREATER
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
#endif
    }
}
