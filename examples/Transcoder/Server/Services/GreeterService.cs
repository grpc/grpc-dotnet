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

namespace Server
{
    public class GreeterService : Greeter.GreeterBase
    {
        private readonly ILogger _logger;

        public GreeterService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<GreeterService>();
        }

        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            _logger.LogInformation($"Sending hello to {request.Name}");
            return Task.FromResult(new HelloReply { Message = $"Hello {request.Name}" });
        }

        public override async Task SayHelloStream(HelloRequestCount request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            if (request.Count <= 0)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Count must be greater than zero."));
            }

            _logger.LogInformation($"Sending {request.Count} hellos to {request.Name}");

            for (var i = 0; i < request.Count; i++)
            {
                await responseStream.WriteAsync(new HelloReply { Message = $"Hello {request.Name} {i + 1}" });
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
