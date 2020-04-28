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
using CommandLine;
using Grpc.Core;
using Grpc.Core.Logging;
using Grpc.Shared.TestAssets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InteropTestsClient
{
    public class Program
    {
        public static void Main(string[] args)
        {
            GrpcEnvironment.SetLogger(new ConsoleLogger());
            var parserResult = Parser.Default.ParseArguments<ClientOptions>(args)
                .WithNotParsed(errors => Environment.Exit(1))
                .WithParsed(options =>
                {
                    Console.WriteLine("Use TLS: " + options.UseTls);
                    Console.WriteLine("Use Test CA: " + options.UseTestCa);
                    Console.WriteLine("Client type: " + options.ClientType);
                    Console.WriteLine("Server host: " + options.ServerHost);
                    Console.WriteLine("Server port: " + options.ServerPort);

                    var services = new ServiceCollection();
                    services.AddLogging(configure =>
                    {
                        configure.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Trace);
                        configure.AddConsole(loggerOptions => loggerOptions.IncludeScopes = true);
                    });

                    using var serviceProvider = services.BuildServiceProvider();

                    var interopClient = new InteropClient(options, serviceProvider.GetRequiredService<ILoggerFactory>());
                    interopClient.Run().Wait();
                });
        }
    }
}
