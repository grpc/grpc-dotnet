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
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Common;
using Count;
using Grpc.Core;
using Grpc.NetCore.HttpClient;

namespace Sample.Clients
{
    class Program
    {
        static Random RNG = new Random();

        static async Task Main(string[] args)
        {
            // Server will only support Https on Windows and Linux
            var certificate = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? null : new X509Certificate2(Path.Combine(Resources.CertDir, "client.crt"));
            var client = GrpcClientFactory.Create<Counter.CounterClient>(@"https://localhost:50051", certificate);

            var reply = client.IncrementCount(new Google.Protobuf.WellKnownTypes.Empty());
            Console.WriteLine("Count: " + reply.Count);

            using (var call = client.AccumulateCount())
            {
                for (int i = 0; i < 3; i++)
                {
                    var count = RNG.Next(5);
                    Console.WriteLine($"Accumulating with {count}");
                    await call.RequestStream.WriteAsync(new CounterRequest { Count = count });
                    await Task.Delay(1000);
                }

                await call.RequestStream.CompleteAsync();
                Console.WriteLine($"Count: {(await call.ResponseAsync).Count}");
            }

            Console.WriteLine("Shutting down");
            //channel.ShutdownAsync().Wait();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
