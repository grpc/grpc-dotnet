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
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Grpc.Core;
using Grpc.Core.Utils;
using Grpc.Testing;

namespace BenchmarkWorkerWebsite
{
    /// <summary>
    /// Helper methods to start server runners for performance testing.
    /// </summary>
    public class ServerRunners
    {
        /// <summary>
        /// Creates a started server runner.
        /// </summary>
        public static IServerRunner CreateStarted(ServerConfig config, ILogger logger)
        {
            logger.LogInformation("ServerConfig: {0}", config);

            var webHostBuilder = WebHost.CreateDefaultBuilder();

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
            if (config.ChannelArgs.Count > 0)
            {
                logger.LogWarning("ServerConfig.ChannelArgs is not supported for C#. Ignoring the value");
            }

            int port = config.Port;
            if (port == 0)
            {
                // TODO(jtattermusch): add support for port autoselection
                port = 50055;
                logger.LogWarning("Grpc.AspNetCore server doesn't support autoselecting of listening port. Setting port explictly to " + port);
            }

            webHostBuilder.ConfigureKestrel((context, options) =>
            {
                options.ListenAnyIP(port, listenOptions =>
                {
                    // TODO(jtattermusch): use TLS if config.SecurityParams != null
                    listenOptions.Protocols = HttpProtocols.Http2;
                });
            });

            if (config.ServerType == ServerType.AsyncServer)
            {
               GrpcPreconditions.CheckArgument(config.PayloadConfig == null,
                   "ServerConfig.PayloadConfig shouldn't be set for BenchmarkService based server.");
               webHostBuilder.UseStartup<BenchmarkServiceStartup>();
            }
            else if (config.ServerType == ServerType.AsyncGenericServer)
            {
               var genericService = new GenericServiceImpl(config.PayloadConfig.BytebufParams.RespSize);
               // TODO(jtattermusch): use startup with given generic service
               throw new ArgumentException("Generice service is not yet implemented.");
            }
            else
            {
               throw new ArgumentException("Unsupported ServerType");
            }

            // Don't log requests handled by the benchmarking service
            webHostBuilder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

            var webHost = webHostBuilder.Build();
            webHost.Start();
            return new ServerRunnerImpl(webHost, logger, port);
        }

        private class BenchmarkServiceStartup
        {
            // This method gets called by the runtime. Use this method to add services to the container.
            // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddGrpc();
            }

            // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
            public void Configure(IApplicationBuilder app)
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGrpcService<BenchmarkServiceImpl>();
                });
            }
        }

        private class GenericServiceImpl
        {
            readonly byte[] response;

            public GenericServiceImpl(int responseSize)
            {
                this.response = new byte[responseSize];
            }

            /// <summary>
            /// Generic streaming call handler.
            /// </summary>
            public async Task StreamingCall(IAsyncStreamReader<byte[]> requestStream, IServerStreamWriter<byte[]> responseStream, ServerCallContext context)
            {
                await requestStream.ForEachAsync(async request =>
                {
                    await responseStream.WriteAsync(response);
                });
            }
        }
    }

    /// <summary>
    /// Server runner.
    /// </summary>
    public class ServerRunnerImpl : IServerRunner
    {
        readonly IWebHost webHost;
        readonly ILogger logger;
        readonly int boundPort;
        readonly TimeStats timeStats = new TimeStats();

        public ServerRunnerImpl(IWebHost webHost, ILogger logger, int boundPort)
        {
            this.webHost = webHost;
            this.logger = logger;
            this.boundPort = boundPort;
        }

        public int BoundPort => boundPort;

        /// <summary>
        /// Gets server stats.
        /// </summary>
        /// <returns>The stats.</returns>
        public ServerStats GetStats(bool reset)
        {
            var timeSnapshot = timeStats.GetSnapshot(reset);

            logger.LogInformation("[ServerRunner.GetStats] GC collection counts: gen0 {0}, gen1 {1}, gen2 {2}, (seconds since last reset {3})",
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
            return webHost.StopAsync();
        }
    }
}
