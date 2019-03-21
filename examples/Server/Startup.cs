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

using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ProtoBuf;
using protobuf_net.Grpc;
using System.ServiceModel;
using System.Threading.Tasks;

namespace GRPCServer
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddGrpc();
            services.AddSingleton<IncrementingCounter>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            app.UseRouting(routes =>
            {
                routes.MapGrpcService<ChatterService>();
                routes.MapGrpcService<CounterService>();
                routes.MapCodeFirstGrpcService<MyService>();
            });
        }
    }
}

[ServiceContract(Name = "Greet.Greeter")] // only needed to explicitly specify service name
class MyService
{
    // note: currently only very specific API signatures are supported, as it needs to match
    // the signature that the underlying google API uses; a +1 feature would be to support
    // alternative signatures, for example:
    // a) ValueTask<HelloReply> SayHelloAsync(HelloRequest request) - ValueTask and no context
    // b) HelloReply SayHelloAsync(ServerCallContext context) - sync and no context
    // c) IAsyncEnumerable<HelloReply> SayHellosAsync(HelloRequest request, ServerCallContext context) - IAsyncEnumerable<T>
    // (or is it Channel<T> ?)

    // The tool would generate the corresponding proxy server/client wrapper to make the magic happens
    // In particular, the intention here is that the API *could* be identical between server and client
    // (although that is not a hard requirement or expectation)
    // 

    public Task<HelloReply> SayHelloAsync(HelloRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HelloReply { Message = $"Hello, {request.Name}" });
    }

    public async Task SayHellosAsync(HelloRequest request, IServerStreamWriter<HelloReply> stream, ServerCallContext serverCallContext)
    {
        for (int i = 0; i < 5; i++)
        {
            await stream.WriteAsync(new HelloReply { Message = $"Hellos {i}, {request.Name}" });
        }
    }

    [ProtoContract]
    public class HelloRequest
    {
        [ProtoMember(1)]
        public string Name { get; set; }
    }
    [ProtoContract]
    public class HelloReply
    {
        [ProtoMember(1)]
        public string Message { get; set; }
    }
}
