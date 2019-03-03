﻿#region Copyright notice and license

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
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Chat;
using Common;
using Grpc.Core;
using Grpc.NetCore.HttpClient;

namespace Sample.Clients
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("No name provided. Usage: dotnet run <name>.");
                return 1;
            }

            var name = args[0];

            // Server will only support Https on Windows and Linux
            var certificate = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? null : new X509Certificate2(Path.Combine(Resources.CertDir, "client.crt"));
            var client = GrpcClientFactory.Create<Chatter.ChatterClient>(@"https://localhost:50051", certificate);

            using (var chat = client.Chat())
            {
                Console.WriteLine($"Connected as {name}. Send empty message to quit.");

                // Dispatch, this could be racy
                var responseTask = Task.Run(async () =>
                {
                    while (await chat.ResponseStream.MoveNext(CancellationToken.None))
                    {
                        Console.WriteLine($"{chat.ResponseStream.Current.Name}: {chat.ResponseStream.Current.Message}");
                    }
                });

                var line = Console.ReadLine();
                while (!string.IsNullOrEmpty(line))
                {
                    await chat.RequestStream.WriteAsync(new ChatMessage { Name = name, Message = line });
                    line = Console.ReadLine();
                }
                await chat.RequestStream.CompleteAsync();
            }

            Console.WriteLine("Shutting down");
            //client.Dispose();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();

            return 0;
        }
    }
}
