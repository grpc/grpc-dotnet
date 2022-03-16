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

using Grpc.AspNetCore.Server.Model;
using Grpc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace QpsWorker.Infrastructure
{
    public class ServerRunner
    {
        public static ServerRunner Start(ILoggerFactory loggerFactory, ServerConfig config)
        {
            var logger = loggerFactory.CreateLogger<ServerRunner>();

            logger.LogDebug("ServerConfig: {0}", config);

            if (config.AsyncServerThreads != 0)
            {
                logger.LogWarning("ServerConfig.AsyncServerThreads is not supported for C#. Ignoring the value");
            }
            if (config.CoreLimit != 0)
            {
                logger.LogWarning("ServerConfig.CoreLimit is not supported for C#. Ignoring the value");
            }
            if (config.CoreList.Count > 0)
            {
                logger.LogWarning("ServerConfig.CoreList is not supported for C#. Ignoring the value");
            }

            var app = BuildApp(config, logger);

            if (config.ServerType == ServerType.AsyncServer)
            {
                if (config.PayloadConfig != null)
                {
                    throw new InvalidOperationException("ServerConfig.PayloadConfig shouldn't be set for BenchmarkService based server.");
                }

                app.MapGrpcService<BenchmarkServiceImpl>();
            }
            else if (config.ServerType == ServerType.AsyncGenericServer)
            {
                // Note: GenericService adds its method via custom method provider.
                app.MapGrpcService<GenericService>();
            }
            else
            {
                throw new InvalidOperationException($"Unsupported ServerType: {config.ServerType}");
            }

            // Don't wait for server to stop.
            _ = app.RunAsync();

            logger.LogInformation("Server started.");

            return new ServerRunner(logger, app);
        }

        private static WebApplication BuildApp(ServerConfig config, ILogger logger)
        {
            var configRoot = ConfigHelpers.GetConfiguration();

            var builder = WebApplication.CreateBuilder();
            var services = builder.Services;

            services.AddGrpc(o =>
            {
                // Small performance benefit to not add catch-all routes to handle UNIMPLEMENTED for unknown services
                o.IgnoreUnknownServices = true;
            });
            services.Configure<RouteOptions>(c =>
            {
                // Small performance benefit to skip checking for security metadata on endpoint
                c.SuppressCheckForUnhandledSecurityMetadata = true;
            });
            services.AddSingleton<BenchmarkServiceImpl>();
            services.AddSingleton<GenericService>(new GenericService(config.PayloadConfig?.BytebufParams?.RespSize ?? 0));
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<GenericService>), typeof(GenericServiceMethodProvider)));

            builder.WebHost.ConfigureKestrel(kestrel =>
            {
                var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                var certPath = Path.Combine(basePath!, "Certs", "server1.pfx");

                kestrel.ListenAnyIP(config.Port, listenOptions =>
                {
                    listenOptions.Protocols = HttpProtocols.Http2;

                    // Contents of "securityParams" are basically ignored.
                    // Instead the server is setup with the default test cert.
                    if (config.SecurityParams != null)
                    {
                        listenOptions.UseHttps(certPath, "1111");
                    }
                });

                // Other gRPC servers don't include a server header
                kestrel.AddServerHeader = false;
            });

            builder.Logging.ClearProviders();
            if (Enum.TryParse<LogLevel>(configRoot["LogLevel"], out var logLevel) && logLevel != LogLevel.None)
            {
                logger.LogInformation($"Console Logging enabled with level '{logLevel}'");
                builder.Logging.AddSimpleConsole(o => o.TimestampFormat = "ss.ffff ").SetMinimumLevel(logLevel);
            }

            var app = builder.Build();
            return app;
        }

        private readonly WebApplication _webApplication;
        private readonly ILogger _logger;
        private readonly TimeStats _timeStats = new TimeStats();

        public ServerRunner(ILogger logger, WebApplication webApplication)
        {
            _logger = logger;
            _webApplication = webApplication;
        }

        public int BoundPort
        {
            get
            {
                var boundUri = new Uri(_webApplication.Urls.Single());
                return boundUri.Port;
            }
        }

        /// <summary>
        /// Gets server stats.
        /// </summary>
        /// <returns>The stats.</returns>
        public ServerStats GetStats(bool reset)
        {
            var timeSnapshot = _timeStats.GetSnapshot(reset);

            _logger.LogInformation("[ServerRunner.GetStats] GC collection counts: gen0 {0}, gen1 {1}, gen2 {2}, (seconds since last reset {3})",
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), timeSnapshot.WallClockTime.TotalSeconds);

            return new ServerStats
            {
                TimeElapsed = timeSnapshot.WallClockTime.TotalSeconds,
                TimeUser = timeSnapshot.UserProcessorTime.TotalSeconds,
                TimeSystem = timeSnapshot.PrivilegedProcessorTime.TotalSeconds
            };
        }

        /// <summary>
        /// Asynchronously stops the server.
        /// </summary>
        /// <returns>Task that finishes when server has shutdown.</returns>
        public Task StopAsync()
        {
            return _webApplication.StopAsync();
        }
    }
}
