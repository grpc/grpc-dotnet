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

using System.Threading.Tasks;
using Grpc.Core;
using Greet;
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
        var httpContext = context.GetHttpContext();
        _logger.LogInformation($"Connection id: {httpContext.Connection.Id}");

        _logger.LogInformation($"Sending hello to {request.Name}");
        return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
    }

    public override async Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
    {
        var httpContext = context.GetHttpContext();
        _logger.LogInformation($"Connection id: {httpContext.Connection.Id}");

        for (int i = 0; i < 3; i++)
        {
            var message = $"How are you {request.Name}? {i}";
            _logger.LogInformation($"Sending greeting {message}");
            await responseStream.WriteAsync(new HelloReply { Message = message });
            // Gotta look busy
            await Task.Delay(1000);
        }

        _logger.LogInformation("Sending goodbye");
        await responseStream.WriteAsync(new HelloReply { Message = $"Goodbye {request.Name}!" });
    }
}
