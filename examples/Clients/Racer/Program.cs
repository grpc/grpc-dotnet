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
using System.Diagnostics;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Race;

namespace Sample.Clients
{
    class Program
    {
        private static readonly TimeSpan RaceDuration = TimeSpan.FromSeconds(30);

        static async Task Main(string[] args)
        {
            var channel = GrpcChannel.ForAddress("https://localhost:50051");
            var client = new Racer.RacerClient(channel);

            Console.WriteLine("Press any key to start race...");
            Console.ReadKey();

            await BidirectionalStreamingExample(client);

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task BidirectionalStreamingExample(Racer.RacerClient client)
        {
            var headers = new Metadata { new Metadata.Entry("race-duration", RaceDuration.ToString()) };

            Console.WriteLine("Ready, set, go!");
            using (var call = client.ReadySetGo(new CallOptions(headers)))
            {

                // Read incoming messages in a background task
                RaceMessage? lastMessageReceived = null;
                var readTask = Task.Run(async () =>
                {
                    await foreach (var message in call.ResponseStream.ReadAllAsync())
                    {
                        lastMessageReceived = message;
                    }
                });

                // Write outgoing messages until timer is complete
                var sw = Stopwatch.StartNew();
                var sent = 0;
                while (sw.Elapsed < RaceDuration)
                {
                    await call.RequestStream.WriteAsync(new RaceMessage { Count = ++sent });
                }

                // Finish call and report results
                await call.RequestStream.CompleteAsync();
                await readTask;

                Console.WriteLine($"Messages sent: {sent}");
                Console.WriteLine($"Messages received: {lastMessageReceived?.Count ?? 0}");
            }
        }
    }
}
