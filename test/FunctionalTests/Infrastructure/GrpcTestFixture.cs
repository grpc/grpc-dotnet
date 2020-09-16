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
using System.Net.Http;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
    {
        private readonly InProcessTestServer _server;

        public GrpcTestFixture(Action<IServiceCollection>? initialConfigureServices = null)
        {
            LoggerFactory = new LoggerFactory();

            Action<IServiceCollection> configureServices = services =>
            {
                // Registers a service for tests to add new methods
                services.AddSingleton<DynamicGrpcServiceRegistry>();
            };

            _server = new InProcessTestServer<TStartup>(services =>
            {
                initialConfigureServices?.Invoke(services);
                configureServices(services);
            });

            _server.StartServer();

            DynamicGrpc = _server.Host!.Services.GetRequiredService<DynamicGrpcServiceRegistry>();

#if !NET5_0
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
#endif

            (Client, Handler) = CreateHttpCore();
        }

        public ILoggerFactory LoggerFactory { get; }
        public DynamicGrpcServiceRegistry DynamicGrpc { get; }

        public HttpMessageHandler Handler { get; }
        public HttpClient Client { get; }

        public HttpClient CreateClient(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null)
        {
            return CreateHttpCore(endpointName, messageHandler).client;
        }

        private (HttpClient client, HttpMessageHandler handler) CreateHttpCore(TestServerEndpointName? endpointName = null, DelegatingHandler? messageHandler = null)
        {
            endpointName ??= TestServerEndpointName.Http2;

            var httpClientHandler = new HttpClientHandler();
            httpClientHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            HttpClient client;
            HttpMessageHandler handler;
            if (messageHandler != null)
            {
                messageHandler.InnerHandler = httpClientHandler;
                handler = messageHandler;
                client = new HttpClient(messageHandler);
            }
            else
            {
                handler = httpClientHandler;
                client = new HttpClient(httpClientHandler);
            }

            if (endpointName == TestServerEndpointName.Http2)
            {
                client.DefaultRequestVersion = new Version(2, 0);
#if NET5_0
                client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher;
#endif
            }
            client.BaseAddress = new Uri(_server.GetUrl(endpointName.Value));

            return (client, handler);
        }

        public Uri GetUrl(TestServerEndpointName? endpointName = null)
        {
            switch (endpointName)
            {
                case TestServerEndpointName.Http1:
                    return new Uri(_server.GetUrl(endpointName.Value));
                case TestServerEndpointName.Http2:
                    return new Uri(_server.GetUrl(endpointName.Value));
                case TestServerEndpointName.Http1WithTls:
                    return new Uri(_server.GetUrl(endpointName.Value));
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
    }
}
