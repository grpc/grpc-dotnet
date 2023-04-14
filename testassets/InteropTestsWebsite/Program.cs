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

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace InteropTestsWebsite;

public class Program
{
    private const LogLevel MinimumLogLevel = LogLevel.Debug;

    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    [UnconditionalSuppressMessage("AotAnalysis", "IL3050:RequiresDynamicCode", 
        Justification = "DependencyInjection only used with safe types.")]
    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddLogging(builder => builder.SetMinimumLevel(MinimumLogLevel));
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel((context, options) =>
                {
                    // Support --port and --use_tls cmdline arguments normally supported
                    // by gRPC interop servers.
                    var http2Port = GetConfigValue<int>(context.Configuration, "port", 50052);
                    var http1Port = GetConfigValue<int>(context.Configuration, "port_http1", -1);
                    var http3Port = GetConfigValue<int>(context.Configuration, "port_http3", -1);
                    var useTls = GetConfigValue<bool>(context.Configuration, "use_tls", false);

                    options.Limits.MinRequestBodyDataRate = null;
                    options.ListenAnyIP(http2Port, o => ConfigureEndpoint(o, useTls, HttpProtocols.Http2));
                    if (http1Port != -1)
                    {
                        options.ListenAnyIP(http1Port, o => ConfigureEndpoint(o, useTls, HttpProtocols.Http1));
                    }
                    if (http3Port != -1)
                    {
                        options.ListenAnyIP(http3Port, o => ConfigureEndpoint(o, useTls, HttpProtocols.Http3));
                    }

                    void ConfigureEndpoint(ListenOptions listenOptions, bool useTls, HttpProtocols httpProtocols)
                    {
                        Console.WriteLine($"Enabling connection encryption: {useTls}");

                        if (useTls)
                        {
                            var basePath = Path.GetDirectoryName(AppContext.BaseDirectory);
                            var certPath = Path.Combine(basePath!, "Certs", "server1.pfx");

                            listenOptions.UseHttps(certPath, "1111");
                        }
                        listenOptions.Protocols = httpProtocols;
                    }
                });
                webBuilder.UseStartup<Startup>();
            });

    [UnconditionalSuppressMessage("TrimmingAnalysis", "IL2026:UnrecognizedReflectionPattern",
        Justification = "Only primitive values retrieved from config.")]
    private static TValue? GetConfigValue<TValue>(IConfiguration configuration, string key, TValue defaultValue)
    {
        return configuration.GetValue<TValue>(key, defaultValue);
    }
}
