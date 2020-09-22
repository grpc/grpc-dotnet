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
using System.Threading;
using System.Threading.Tasks;
using Aggregate;
using Count;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

namespace Client
{
    public class Program
    {
        private static readonly Random Random = new Random();

        static async Task Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Aggregator.AggregatorClient(channel);

            await ServerStreamingCallExample(client);

            await ClientStreamingCallExample(client);

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task ServerStreamingCallExample(Aggregator.AggregatorClient client)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(3.5));

            using var call = client.SayHellos(new HelloRequest { Name = "AggregatorClient" }, cancellationToken: cts.Token);
            try
            {
                await foreach (var message in call.ResponseStream.ReadAllAsync())
                {
                    Console.WriteLine("Greeting: " + message.Message);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                Console.WriteLine("Stream cancelled.");
            }
        }

        private static async Task ClientStreamingCallExample(Aggregator.AggregatorClient client)
        {
            using var call = client.AccumulateCount();
            for (var i = 0; i < 3; i++)
            {
                var count = Random.Next(5);
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
