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

using System.Diagnostics;
using Greet;
using Grpc.Core;
using Grpc.Net.Client;

await MakeInternalCall("https://localhost:5001");
try
{
    await MakeInternalCall("http://localhost:5000");
    Debug.Fail("Expected error.");
}
catch (RpcException ex)
{
    Console.WriteLine(ex.Status.StatusCode);
}

await MakeExternalCall("http://localhost:5000");
try
{
    await MakeExternalCall("https://localhost:5001");
    Debug.Fail("Expected error.");
}
catch (RpcException ex)
{
    Console.WriteLine(ex.Status.StatusCode);
}

Console.WriteLine("Shutting down");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

static async Task MakeInternalCall(string address)
{
    var channel = GrpcChannel.ForAddress(address);
    var client = new Internal.InternalClient(channel);

    var reply = await client.SayHelloAsync(new InternalRequest { Name = "InternalClient" });
    Console.WriteLine("Greeting: " + reply.Message);
}

static async Task MakeExternalCall(string address)
{
    var channel = GrpcChannel.ForAddress(address);
    var client = new External.ExternalClient(channel);

    var reply = await client.SayHelloAsync(new ExternalRequest { Name = "ExternalClient" });
    Console.WriteLine("Greeting: " + reply.Message);
}
