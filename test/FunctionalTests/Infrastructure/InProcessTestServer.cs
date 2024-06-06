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
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure;

public abstract class InProcessTestServer : IDisposable
{
    internal abstract event Action<LogRecord> ServerLogged;

    public abstract string GetUrl(TestServerEndpointName endpointName);

    public abstract IWebHost? Host { get; }

    public abstract void StartServer();

    public abstract void Dispose();
}

public class InProcessTestServer<TStartup> : InProcessTestServer
    where TStartup : class
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly LogSinkProvider _logSinkProvider;
    private readonly Action<IServiceCollection> _initialConfigureServices;
    private readonly Action<WebHostBuilderContext, KestrelServerOptions, IDictionary<TestServerEndpointName, EndpointInfoContainerBase>> _configureKestrel;
    private IWebHost? _host;
    private IHostApplicationLifetime? _lifetime;
    private Dictionary<TestServerEndpointName, EndpointInfoContainerBase>? _urls;

    internal override event Action<LogRecord> ServerLogged
    {
        add => _logSinkProvider.RecordLogged += value;
        remove => _logSinkProvider.RecordLogged -= value;
    }

    public override string GetUrl(TestServerEndpointName endpointName)
    {
        if (_urls == null)
        {
            throw new InvalidOperationException();
        }

        return _urls[endpointName].Address;
    }

    public override IWebHost? Host => _host;

    public InProcessTestServer(Action<IServiceCollection> initialConfigureServices, Action<WebHostBuilderContext, KestrelServerOptions, IDictionary<TestServerEndpointName, EndpointInfoContainerBase>> configureKestrel)
    {
        _logSinkProvider = new LogSinkProvider();
        _loggerFactory = new LoggerFactory();
        _loggerFactory.AddProvider(_logSinkProvider);
        _logger = _loggerFactory.CreateLogger<InProcessTestServer<TStartup>>();

        _initialConfigureServices = initialConfigureServices;
        _configureKestrel = configureKestrel;
    }

    public override void StartServer()
    {
        _urls = new Dictionary<TestServerEndpointName, EndpointInfoContainerBase>();

        var builder = new WebHostBuilder()
            .ConfigureLogging(builder => builder
                .SetMinimumLevel(LogLevel.Trace)
                .AddProvider(new ForwardingLoggerProvider(_loggerFactory)))
            .ConfigureServices(services =>
            {
                _initialConfigureServices?.Invoke(services);
            })
            .UseStartup(typeof(TStartup))
            .UseKestrel((context, options) =>
            {
                _configureKestrel(context, options, _urls);
            })
            .UseContentRoot(Directory.GetCurrentDirectory());

        _host = builder.Build();

        var t = Task.Run(() => _host.Start());
        _logger.LogInformation("Starting test server...");
        _lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();

        // This only happens once per fixture, so we can afford to wait a little bit on it.
        if (!_lifetime.ApplicationStarted.WaitHandle.WaitOne(TimeSpan.FromSeconds(20)))
        {
            // t probably faulted
            if (t.IsFaulted)
            {
                throw t.Exception!.InnerException!;
            }

            var logs = _logSinkProvider.GetLogs();
            throw new TimeoutException($"Timed out waiting for application to start.{Environment.NewLine}Startup Logs:{Environment.NewLine}{RenderLogs(logs)}");
        }
        _logger.LogInformation("Test Server started");

        _lifetime.ApplicationStopped.Register(() =>
        {
            _logger.LogInformation("Test server shut down");
        });
    }

    private string RenderLogs(IList<LogRecord> logs)
    {
        var builder = new StringBuilder();
        foreach (var log in logs)
        {
            var s = $"{log.Timestamp:O} {log.LoggerName} {log.LogLevel}: {log.Formatter(log.State, log.Exception)}";
            builder.AppendLine(s);
            if (log.Exception != null)
            {
                var message = log.Exception.ToString();
                foreach (var line in message.Split(new[] { Environment.NewLine }, StringSplitOptions.None))
                {
                    s = $"| {line}";
                    builder.AppendLine(s);
                }
            }
        }
        return builder.ToString();
    }

    public override void Dispose()
    {
        _logger.LogInformation("Shutting down test server");
        _host?.Dispose();
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
