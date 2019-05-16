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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Common;
using Greet;
using Grpc.Core;

namespace Sample.Clients
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Server will only support Https on Windows and Linux
            var credentials = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? ChannelCredentials.Insecure : ClientResources.SslCredentials;
            var channel = new Channel("localhost:50051", credentials);
            var client = new Greeter.GreeterClient(channel);

            await UnaryCallExample(client);

            await ServerStreamingCallExample(client);

            Console.WriteLine("Shutting down");
            await channel.ShutdownAsync();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task UnaryCallExample(Greeter.GreeterClient client)
        {
            var reply = await client.SayHelloAsync(new HelloRequest { Name = "GreeterClient" });
            Console.WriteLine("Greeting: " + reply.Message);
        }

        private static async Task ServerStreamingCallExample(Greeter.GreeterClient client)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(3.5));

            using (var replies = client.SayHellos(new HelloRequest { Name = "GreeterClient" }, cancellationToken: cts.Token))
            {
                try
                {
                    while (await replies.ResponseStream.MoveNext(cts.Token))
                    {
                        Console.WriteLine("Greeting: " + replies.ResponseStream.Current.Message);
                    }
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                {
                    Console.WriteLine("Stream cancelled.");
                }
            }
        }
    }
}