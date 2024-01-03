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
using Microsoft.Extensions.DependencyInjection;
using Unimplemented;

// This app tests clients created directly from channel and clients created from factory.
// Because of the vagaries of trimming, there is a small chance that testing both in the same app could
// cause them to work when alone they might fail. Consider splitting into different client apps.

try
{
    if (args.Length != 1 || !int.TryParse(args[0], out var port))
    {
        throw new Exception("Port must be passed as an argument.");
    }

    var address = new Uri($"http://localhost:{port}");

    // Basic channel
    using var channel = GrpcChannel.ForAddress(address);
    await CallGreeter(new Greeter.GreeterClient(channel));
    await CallUnimplemented(new UnimplementedService.UnimplementedServiceClient(channel));

    // Client factory
    var services = new ServiceCollection();
    services.AddGrpcClient<Greeter.GreeterClient>(op =>
    {
        op.Address = address;
    });
    services.AddGrpcClient<UnimplementedService.UnimplementedServiceClient>(op =>
    {
        op.Address = address;
    });
    var serviceProvider = services.BuildServiceProvider();

    await CallGreeter(serviceProvider.GetRequiredService<Greeter.GreeterClient>());
    await CallUnimplemented(serviceProvider.GetRequiredService<UnimplementedService.UnimplementedServiceClient>());

    Console.WriteLine("Shutting down");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}

static async Task CallGreeter(Greeter.GreeterClient client)
{
    var reply = await client.SayHelloAsync(new HelloRequest { Name = "GreeterClient" });
    Console.WriteLine("Greeting: " + reply.Message);
}

static async Task CallUnimplemented(UnimplementedService.UnimplementedServiceClient client)
{
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
