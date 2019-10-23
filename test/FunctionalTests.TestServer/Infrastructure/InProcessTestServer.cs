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
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public abstract class InProcessTestServer : IDisposable
    {
        internal abstract event Action<LogRecord> ServerLogged;

        public abstract string GetUrl(TestServerEndpointName endpointName);

        public abstract IWebHost? Host { get; }

        public abstract void StartServer();

        public abstract void Dispose();
        
        public abstract HttpMessageHandler CreateHandler();
    }

    public class InProcessTestServer<TStartup> : InProcessTestServer
        where TStartup : class
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        private readonly LogSinkProvider _logSinkProvider;
        private readonly Action<IServiceCollection>? _initialConfigureServices;
        private IWebHost? _host;
        private IHostApplicationLifetime? _lifetime;
        private TestServer? _server;

        internal override event Action<LogRecord> ServerLogged
        {
            add => _logSinkProvider.RecordLogged += value;
            remove => _logSinkProvider.RecordLogged -= value;
        }

        public override string GetUrl(TestServerEndpointName endpointName) => "http://127.0.0.1/";

        public override IWebHost? Host => _host;

        public InProcessTestServer(Action<IServiceCollection>? initialConfigureServices)
        {
            _logSinkProvider = new LogSinkProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_logSinkProvider);
            _logger = _loggerFactory.CreateLogger<InProcessTestServer<TStartup>>();

            _initialConfigureServices = initialConfigureServices;
        }

        public override void StartServer()
        {
            _server = new TestServer(new WebHostBuilder()
                .ConfigureLogging(builder => builder
                    .SetMinimumLevel(LogLevel.Trace)
                    .AddProvider(new ForwardingLoggerProvider(_loggerFactory)))
                .ConfigureServices(services => { _initialConfigureServices?.Invoke(services); })
                .UseStartup(typeof(TStartup))
                .UseContentRoot(Directory.GetCurrentDirectory()));

            _host = _server.Host;

            _logger.LogInformation("Starting test server...");
            _lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();

            _logger.LogInformation("Test Server started");

            _lifetime.ApplicationStopped.Register(() =>
            {
                _logger.LogInformation("Test server shut down");
            });
        }

        public override HttpMessageHandler CreateHandler() => _server?.CreateHandler() ?? new HttpClientHandler();

        public override void Dispose()
        {
            _logger.LogInformation("Shutting down test server");
            _server?.Dispose();
            _loggerFactory.Dispose();
        }

        private class ForwardingLoggerProvider : ILoggerProvider
        {
            private readonly ILoggerFactory _loggerFactory;

            public ForwardingLoggerProvider(ILoggerFactory loggerFactory)
            {
                _loggerFactory = loggerFactory;
            }

            public void Dispose()
            {
            }

            public ILogger CreateLogger(string categoryName)
            {
                return _loggerFactory.CreateLogger(categoryName);
            }
        }
    }
}
