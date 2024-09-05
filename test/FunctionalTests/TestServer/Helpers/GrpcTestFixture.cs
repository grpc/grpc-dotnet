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

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Tests.FunctionalTests.Helpers;

public delegate void LogMessage(LogLevel logLevel, string categoryName, EventId eventId, string message, Exception? exception);

public class GrpcTestFixture<TStartup> : IDisposable where TStartup : class
{
    private readonly TestServer _server;
    private readonly IHost _host;

    public event LogMessage? LoggedMessage;

    public GrpcTestFixture() : this(null) { }

    public GrpcTestFixture(Action<IServiceCollection>? initialConfigureServices)
    {
        LoggerFactory = new LoggerFactory();
        LoggerFactory.AddProvider(new ForwardingLoggerProvider((logLevel, category, eventId, message, exception) =>
        {
            LoggedMessage?.Invoke(logLevel, category, eventId, message, exception);
        }));

        var builder = new HostBuilder()
            .ConfigureServices(services =>
            {
                initialConfigureServices?.Invoke(services);
                services.AddSingleton<ILoggerFactory>(LoggerFactory);
            })
            .ConfigureWebHostDefaults(webHost =>
            {
                webHost
                    .UseTestServer()
                    .UseStartup<TStartup>();
            });
        _host = builder.Start();
        _server = _host.GetTestServer();

        // Need to set the response version to 2.0.
        // Required because of this TestServer issue - https://github.com/aspnet/AspNetCore/issues/16940
        var handler = new ResponseVersionHandler();
        handler.InnerHandler = _server.CreateHandler();

        var client = new HttpClient(handler);
        client.BaseAddress = new Uri("http://localhost");

        Client = client;
    }

    public LoggerFactory LoggerFactory { get; }

    public HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        _host.Dispose();
        _server.Dispose();
    }

    private class ResponseVersionHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken);
            response.Version = request.Version;

            return response;
        }
    }

    public IDisposable GetTestContext()
    {
        return new GrpcTestContext<TStartup>(this);
    }
}
