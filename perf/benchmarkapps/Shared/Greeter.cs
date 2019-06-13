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
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Greet;
using Grpc.Core;

class GreeterService : Greeter.GreeterBase
{
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        return Task.FromResult(new HelloReply
        {
            Message = "Hello " + request.Name,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    public override async Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
    {
        for (var i = 0; i < 3; i++)
        {
            var message = $"How are you {request.Name}? {i}";
            await responseStream.WriteAsync(new HelloReply
            {
                Message = message,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        await responseStream.WriteAsync(new HelloReply
        {
            Message = $"Goodbye {request.Name}!",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }
}