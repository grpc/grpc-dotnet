// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Greet;
using Grpc.Core;
using Microsoft.Extensions.Logging;

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

        public override Task<HelloReply> SayHelloFrom(HelloRequestFrom request, ServerCallContext context)
        {
            _logger.LogInformation($"Sending hello to {request.Name} from {request.From}");
            return Task.FromResult(new HelloReply { Message = $"Hello {request.Name} from {request.From}" });
        }
    }
}
