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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Streaming;
using Test;

namespace FunctionalTestsWebsite.Services
{
    public class TesterService : Tester.TesterBase
    {
        private readonly ILogger _logger;

        public TesterService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<TesterService>();
        }

        public override Task<HelloReply> SayHelloUnary(HelloRequest request, ServerCallContext context)
        {
            _logger.LogInformation($"Sending hello to {request.Name}");
            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }

        public override async Task<HelloReply> SayHelloUnaryError(HelloRequest request, ServerCallContext context)
        {
            await Task.Yield();

            throw new RpcException(new Status(StatusCode.NotFound, string.Empty));
        }

        public override async Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            var i = 0;
            while (!context.CancellationToken.IsCancellationRequested)
            {
                var message = $"How are you {request.Name}? {++i}";
                _logger.LogInformation($"Sending greeting {message}.");

                await responseStream.WriteAsync(new HelloReply { Message = message });

                // Gotta look busy
                await Task.Delay(1000);
            }
        }

        public override async Task SayHelloServerStreamingError(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await responseStream.WriteAsync(new HelloReply());

            throw new RpcException(new Status(StatusCode.NotFound, string.Empty));
        }

        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            var names = new List<string>();

            await foreach (var message in requestStream.ReadAllAsync())
            {
                names.Add(message.Name);
            }

            return new HelloReply { Message = "Hello " + string.Join(", ", names) };
        }

        public override async Task<HelloReply> SayHelloClientStreamingError(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            await Task.Yield();

            throw new RpcException(new Status(StatusCode.NotFound, string.Empty));
        }

        public override async Task SayHelloBidirectionalStreaming(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(new HelloReply { Message = "Hello " + message.Name });
            }
        }

        public override async Task SayHelloBidirectionalStreamingError(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(new HelloReply { Message = "Hello " + message.Name });
            }

            throw new RpcException(new Status(StatusCode.NotFound, string.Empty));
        }
    }
}