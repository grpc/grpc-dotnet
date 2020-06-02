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
using System.Threading.Tasks;
using Common;
using Grpc.Core;
using Grpc.Testing;
using Microsoft.Extensions.Configuration;

namespace GrpcCoreServer
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine($"Args: {string.Join(' ', args)}");

            var config = new ConfigurationBuilder()
              .AddJsonFile("hosting.json", optional: true)
              .AddEnvironmentVariables(prefix: "ASPNETCORE_")
              .AddCommandLine(args)
              .Build();

            try
            {
                await RunAsync(config);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Console.WriteLine("Global error thrown. Exiting");
                throw;
            }
        }

        private static async Task RunAsync(IConfigurationRoot config)
        {
            var protocol = config["protocol"] ?? string.Empty;
            if (!protocol.Equals("h2c", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only h2c is supported by C-core benchmark server.");
            }

            var address = config.CreateBindingAddress();

            Console.WriteLine($"Starting C-core server listening on {address.Host}:{address.Port}");

            Server server = new Server
            {
                Services =
                {
                    BenchmarkService.BindService(new BenchmarkServiceImpl())
                },
                Ports =
                {
                    // C-core benchmarks currently only support insecure (h2c)
                    { address.Host, address.Port, ServerCredentials.Insecure }
                }
            };
            server.Start();

            Console.WriteLine("Application started.");
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            await server.ShutdownAsync();
        }
    }
}
