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
using Greet;
using Grpc.Core;
using Microsoft.Extensions.Logging;

class GreeterService : Greeter.GreeterBase
{
    private readonly ILogger _logger;

    public GreeterService(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<GreeterService>();
    }

    //Server side handler of the SayHello RPC
    public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
    {
        _logger.LogInformation($"Sending hello to {request.Name}");
        return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
    }

    public override async Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
    {
        for (var i = 0; i < 3; i++)
        {
            // Gotta look busy
            await Task.Delay(100);

            var message = $"How are you {request.Name}? {i}";
            _logger.LogInformation($"Sending greeting {message}");
            await responseStream.WriteAsync(new HelloReply { Message = message });
        }

        // Gotta look busy
        await Task.Delay(100);

        _logger.LogInformation("Sending goodbye");
        await responseStream.WriteAsync(new HelloReply { Message = $"Goodbye {request.Name}!" });
    }

    public override Task SayHellosSendHeadersFirst(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
    {
        context.WriteResponseHeadersAsync(null);

        return SayHellos(request, responseStream, context);
    }

    public override Task<HelloReply> SayHelloSendHeadersTwice(HelloRequest request, ServerCallContext context)
    {
        context.WriteResponseHeadersAsync(null);

        try
        {
            context.WriteResponseHeadersAsync(null);
        }
        catch (InvalidOperationException e) when (e.Message == "Response headers can only be sent once per call.")
        {
            return Task.FromResult(new HelloReply { Message = "Exception validated" });
        }

        return Task.FromResult(new HelloReply { Message = "No exception thrown" });
    }
}
