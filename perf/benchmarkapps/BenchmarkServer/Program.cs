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
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BenchmarkServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args)
        {
            Console.WriteLine();
            Console.WriteLine("ASP.NET Core gRPC Benchmarks");
            Console.WriteLine("----------------------------");

            Console.WriteLine($"Current directory: {Directory.GetCurrentDirectory()}");
            Console.WriteLine($"WebHostBuilder loading from: {typeof(WebHostBuilder).GetTypeInfo().Assembly.Location}");

            var config = new ConfigurationBuilder()
                .AddJsonFile("hosting.json", optional: true)
                .AddEnvironmentVariables(prefix: "ASPNETCORE_")
                .AddCommandLine(args)
                .Build();

            var webHostBuilder =
                WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseConfiguration(config)
                .UseStartup<Startup>()
                .ConfigureLogging(loggerFactory =>
                {
                    loggerFactory.ClearProviders();

                    if (Enum.TryParse(config["LogLevel"], out LogLevel logLevel))
                    {
                        Console.WriteLine($"Console Logging enabled with level '{logLevel}'");
                        loggerFactory.AddConsole().SetMinimumLevel(logLevel);
                    }
                })
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = context.HostingEnvironment.IsDevelopment();
                })
                .ConfigureKestrel((context, options) =>
                {
                    var endPoint = config.CreateIPEndPoint();

                    options.Listen(endPoint, listenOptions =>
                    {
                        var useTls = Convert.ToBoolean(context.Configuration["UseTls"]);
                        Console.WriteLine($"Enabling connection encryption: {useTls}");

                        if (useTls)
                        {
                            var requireClientCertificate = Convert.ToBoolean(context.Configuration["RequireClientCertificate"]);
                            Console.WriteLine($"Require client certificate: {requireClientCertificate}");

                            var basePath = Path.GetDirectoryName(typeof(Program).Assembly.Location);
                            var certPath = Path.Combine(basePath, "Certs/server.pfx");

                            listenOptions.UseHttps(certPath, "1111", httpsOptions =>
                            {
                                if (requireClientCertificate)
                                {
                                    httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                }
                            });
                        }

                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                });

            return webHostBuilder;
        }
    }
}
