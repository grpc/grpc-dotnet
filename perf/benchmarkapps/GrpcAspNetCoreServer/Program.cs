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
using System.IO;
using System.Reflection;
using Common;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GrpcAspNetCoreServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("ASP.NET Core gRPC Benchmarks");
            Console.WriteLine("----------------------------");
            Console.WriteLine($"Args: {string.Join(' ', args)}");
            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"WebHostBuilder loading from: {typeof(WebHostBuilder).GetTypeInfo().Assembly.Location}");

            var config = new ConfigurationBuilder()
                .AddJsonFile("hosting.json", optional: true)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            var hostBuilder = Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseContentRoot(Directory.GetCurrentDirectory());
                    webBuilder.UseConfiguration(config);
                    webBuilder.UseStartup<Startup>();

                    webBuilder.ConfigureKestrel((context, options) =>
                    {
                        var endPoint = config.CreateIPEndPoint();

                        // ListenAnyIP will work with IPv4 and IPv6.
                        // Chosen over Listen+IPAddress.Loopback, which would have a 2 second delay when
                        // creating a connection on a local Windows machine.
                        options.ListenAnyIP(endPoint.Port, listenOptions =>
                        {
                            var protocol = config["protocol"] ?? "";
                            bool.TryParse(config["enableCertAuth"], out var enableCertAuth);

                            Console.WriteLine($"Address: {endPoint.Address}:{endPoint.Port}, Protocol: {protocol}");
                            Console.WriteLine($"Certificate authentication: {enableCertAuth}");

                            if (protocol.Equals("h2", StringComparison.OrdinalIgnoreCase))
                            {
                                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;

                                var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                                var certPath = Path.Combine(basePath!, "Certs/testCert.pfx");
                                listenOptions.UseHttps(certPath, "testPassword", httpsOptions =>
                                {
                                    if (enableCertAuth)
                                    {
                                        httpsOptions.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.AllowCertificate;
                                        httpsOptions.AllowAnyClientCertificate();
                                    }
                                });
                            }
                            else if (protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
                            {
                                listenOptions.Protocols = HttpProtocols.Http2;
                            }
                            else if (protocol.Equals("http1", StringComparison.OrdinalIgnoreCase))
                            {
                                listenOptions.Protocols = HttpProtocols.Http1;
                            }
                            else
                            {
                                throw new InvalidOperationException($"Unexpected protocol: {protocol}");
                            }
                        });
                    });
                })
                .ConfigureLogging(loggerFactory =>
                {
                    loggerFactory.ClearProviders();

                    if (Enum.TryParse<LogLevel>(config["LogLevel"], out var logLevel) && logLevel != LogLevel.None)
                    {
                        Console.WriteLine($"Console Logging enabled with level '{logLevel}'");

                        loggerFactory
#if NET5_0
                            .AddSimpleConsole(o => o.TimestampFormat = "ss.ffff ")
#else
                            .AddConsole(o => o.TimestampFormat = "ss.ffff ")
#endif
                            .SetMinimumLevel(logLevel);
                    }
                })
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
                });

            return hostBuilder;
        }
    }
}
