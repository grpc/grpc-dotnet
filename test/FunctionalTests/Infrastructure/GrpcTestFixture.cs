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
using FunctionalTestsWebsite.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
    {
        private readonly TestServer _server;

        public GrpcTestFixture() : this(null) { }

        public GrpcTestFixture(Action<IServiceCollection>? initialConfigureServices)
        {
            LoggerFactory = new LoggerFactory();

            Action<IServiceCollection> configureServices = services =>
            {
                // Registers a service for tests to add new methods
                services.AddSingleton<DynamicGrpcServiceRegistry>();

                services.AddSingleton<ILoggerFactory>(LoggerFactory);

                services.AddSingleton<IPrimaryMessageHandlerProvider, TestPrimaryMessageHandlerProvider>(s => new TestPrimaryMessageHandlerProvider(_server));
            };

            var builder = new WebHostBuilder()
                .ConfigureServices(services =>
                {
                    initialConfigureServices?.Invoke(services);
                    configureServices(services);
                })
                .UseStartup<TStartup>();

            _server = new TestServer(builder);

            DynamicGrpc = _server.Host.Services.GetRequiredService<DynamicGrpcServiceRegistry>();

            Client = _server.CreateClient();
            Client.BaseAddress = new Uri("http://localhost");
        }

        public LoggerFactory LoggerFactory { get; }
        public DynamicGrpcServiceRegistry DynamicGrpc { get; }

        public HttpClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _server.Dispose();
        }
    }
}
