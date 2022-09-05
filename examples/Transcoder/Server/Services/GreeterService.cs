// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
