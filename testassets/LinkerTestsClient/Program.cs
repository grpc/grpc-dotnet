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

using Greet;
using Grpc.Core;
using Grpc.Net.Client;
using Unimplemented;

namespace Client
{
    public class Program
    {
        static async Task<int> Main(string[] args)
        {
            try
            {
                if (args.Length != 1 || !int.TryParse(args[0], out var port))
                {
                    throw new Exception("Port must be passed as an argument.");
                }

                using var channel = GrpcChannel.ForAddress($"http://localhost:{port}");
                await CallGreeter(channel);
                await CallUnimplemented(channel);

                Console.WriteLine("Shutting down");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static async Task CallGreeter(GrpcChannel channel)
        {
            var client = new Greeter.GreeterClient(channel);

            var reply = await client.SayHelloAsync(new HelloRequest { Name = "GreeterClient" });
            Console.WriteLine("Greeting: " + reply.Message);
        }

        private static async Task CallUnimplemented(GrpcChannel channel)
        {
            var client = new UnimplementedService.UnimplementedServiceClient(channel);

            var reply = client.DuplexData();

            try
            {
                await reply.ResponseStream.MoveNext();
                throw new Exception("Expected error status.");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
            {
                Console.WriteLine("Unimplemented status correctly returned.");
            }
        }
    }
}
