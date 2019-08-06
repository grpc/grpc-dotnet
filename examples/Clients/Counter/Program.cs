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
using System.Net.Http;
using System.Threading.Tasks;
using Common;
using Count;
using Grpc.Net.Client;

namespace Sample.Clients
{
    class Program
    {
        static Random RNG = new Random();

        static async Task Main(string[] args)
        {
            var httpClient = ClientResources.CreateHttpClient("localhost:50051");
            var channelBuilder = ChannelBuilder.ForHttpClient(httpClient);
            var client = new Counter.CounterClient(channelBuilder.Build());

            await UnaryCallExample(client);

            await ClientStreamingCallExample(client);

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task UnaryCallExample(Counter.CounterClient client)
        {
            var reply = await client.IncrementCountAsync(new Google.Protobuf.WellKnownTypes.Empty());
            Console.WriteLine("Count: " + reply.Count);
        }

        private static async Task ClientStreamingCallExample(Counter.CounterClient client)
        {
            using (var call = client.AccumulateCount())
            {
                for (var i = 0; i < 3; i++)
                {
                    var count = RNG.Next(5);
                    Console.WriteLine($"Accumulating with {count}");
                    await call.RequestStream.WriteAsync(new CounterRequest { Count = count });
                    await Task.Delay(2000);
                }

                await call.RequestStream.CompleteAsync();

                var response = await call;
                Console.WriteLine($"Count: {response.Count}");
            }
        }
    }
}
