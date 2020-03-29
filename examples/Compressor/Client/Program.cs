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
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

namespace Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Greeter.GreeterClient(channel);

            await UnaryCallExample(client);

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static async Task UnaryCallExample(Greeter.GreeterClient client)
        {
            // 'grpc-internal-encoding-request' is a special metadata value that tells
            // the client to compress the request. This metadata is not sent as a header.
            var metadata = new Metadata();
            metadata.Add("grpc-internal-encoding-request", "gzip");

            var reply = await client.SayHelloAsync(new HelloRequest { Name = "GreeterClient" }, headers: metadata);
            Console.WriteLine("Greeting: " + reply.Message);
        }
    }
}
