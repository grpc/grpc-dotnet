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
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.FunctionalTests
{
    public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
    {
        private readonly TestServer _server;
        private readonly IHost _host;

        public GrpcTestFixture() : this(null) { }

        public GrpcTestFixture(Action<IServiceCollection>? initialConfigureServices)
        {
            LoggerFactory = new LoggerFactory();

            var builder = new HostBuilder()
                .ConfigureServices(services =>
                {
                    initialConfigureServices?.Invoke(services);
                    services.TryAddSingleton<ILoggerFactory>(LoggerFactory);
                })
                .ConfigureWebHostDefaults(webHost =>
                {
                    webHost
                        .UseTestServer()
                        .UseStartup<TStartup>();
                });
            _host = builder.Start();
            _server = _host.GetTestServer();

            Client = _server.CreateClient();
            Client.BaseAddress = new Uri("http://localhost");
        }

        public LoggerFactory LoggerFactory { get; }

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _host.Dispose();
            _server.Dispose();
        }
    }
}
