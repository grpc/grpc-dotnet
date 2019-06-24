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
using System.Collections.Concurrent;
using System.Net.Http;
using FunctionalTestsWebsite.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
    {
        private readonly ILogger _logger;
        private readonly InProcessTestServer _server;
        private readonly object _lock = new object();
        private readonly ConcurrentDictionary<string, ILogger> _serverLoggers;
        private bool _disposed;

        public GrpcTestFixture(Action<IServiceCollection>? initialConfigureServices = null)
        {
            LoggerFactory = new LoggerFactory();
            _logger = LoggerFactory.CreateLogger<GrpcTestFixture<TStartup>>();

            _serverLoggers = new ConcurrentDictionary<string, ILogger>(StringComparer.Ordinal);

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
            _server.ServerLogged += ServerFixtureOnServerLogged;

            _server.StartServer();

            DynamicGrpc = _server.Host!.Services.GetRequiredService<DynamicGrpcServiceRegistry>();

            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

            Client = new HttpClient();
            Client.DefaultRequestVersion = new Version(2, 0);
            Client.BaseAddress = new Uri(_server.Url!);
        }

        private void ServerFixtureOnServerLogged(LogRecord logRecord)
        {
            if (logRecord == null)
            {
                _logger.LogWarning("Server log has no data.");
                return;
            }

            ILogger logger;

            lock (_lock)
            {
                if (_disposed)
                {
                    return;
                }

                // Create (or get) a logger with the same name as the server logger
                // Call in the lock to avoid ODE where LoggerFactory could be disposed by the wrapped disposable
                logger = _serverLoggers.GetOrAdd(logRecord.LoggerName, loggerName => LoggerFactory.CreateLogger("SERVER " + loggerName));
            }

            logger.Log(logRecord.LogLevel, logRecord.EventId, logRecord.State, logRecord.Exception, logRecord.Formatter);
        }

        public LoggerFactory LoggerFactory { get; }
        public DynamicGrpcServiceRegistry DynamicGrpc { get; }

        public HttpClient Client { get; }

        public void Dispose()
        {
            _server.ServerLogged -= ServerFixtureOnServerLogged;
            Client.Dispose();
            _server.Dispose();

            _disposed = true;
        }
    }
}
