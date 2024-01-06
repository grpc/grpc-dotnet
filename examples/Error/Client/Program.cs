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

using Google.Rpc;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new Greeter.GreeterClient(channel);

Console.WriteLine("Hello world app");
Console.WriteLine("===============");

while (true)
{
    Console.WriteLine();

    Console.Write("Enter name: ");
    var name = Console.ReadLine();

    try
    {
        var reply = await client.SayHelloAsync(new HelloRequest { Name = name });
        Console.WriteLine("Greeting: " + reply.Message);
    }
    catch (RpcException ex)
    {
        Console.WriteLine($"Server error: {ex.Status.Detail}");

        var badRequest = ex.GetRpcStatus()?.GetDetail<BadRequest>();
        if (badRequest != null)
        {
            foreach (var fieldViolation in badRequest.FieldViolations)
            {
                Console.WriteLine($"Field: {fieldViolation.Field}");
                Console.WriteLine($"Description: {fieldViolation.Description}");
            }
        }
    }
}
