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
using System.Collections.Generic;
using System.IO;
using Common;
using Grpc.Core;
using Microsoft.Extensions.Configuration;

namespace NativeServer
{
    class Program
    {
        private const bool UseCertificate = false;
        private const bool RequireClientCertificate = false;

        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
               .AddJsonFile("hosting.json", optional: true)
               .AddEnvironmentVariables(prefix: "ASPNETCORE_")
               .AddCommandLine(args)
               .Build();

            var endpoint = config.CreateIPEndPoint();
            var host = endpoint.Address.ToString();

            Console.WriteLine($"Starting nativeServer listening on {host}:{endpoint.Port}");

            Server server = new Server
            {
                Services = { Greet.Greeter.BindService(new GreeterService()) },
                Ports =
                {
                    { host, endpoint.Port, UseCertificate ? GetCertificateCredentials() : ServerCredentials.Insecure }
                }
            };
            server.Start();

            Console.WriteLine("Started!");
            Console.WriteLine("Press any key to stop the server...");
            Console.ReadKey();

            server.ShutdownAsync().Wait();
        }

        private static ServerCredentials GetCertificateCredentials()
        {
            var pair = new List<KeyCertificatePair>
                {
                    new KeyCertificatePair(File.ReadAllText(@"Certs\server.crt"), File.ReadAllText(@"Certs\server.key"))
                };
            
            return RequireClientCertificate
                ? new SslServerCredentials(pair, File.ReadAllText(@"Certs\ca.crt"), SslClientCertificateRequestType.RequestAndRequireAndVerify)
                : new SslServerCredentials(pair);
        }
    }
}
