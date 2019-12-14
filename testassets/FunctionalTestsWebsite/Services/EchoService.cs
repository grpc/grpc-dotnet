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
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Gateway.Testing;

namespace FunctionalTestsWebsite.Services
{
    /// <summary>
    /// Written to as closely as possible mirror the behaviour of the C++ implementation in grpc/grpc-web:
    /// https://github.com/grpc/grpc-web/blob/92aa9f8fc8e7af4aadede52ea075dd5790a63b62/net/grpc/gateway/examples/echo/echo_service_impl.cc
    /// </summary>
    public class EchoService : Grpc.Gateway.Testing.EchoService.EchoServiceBase
    {
        public override Task<EchoResponse> Echo(EchoRequest request, ServerCallContext context)
        {
            return Task.FromResult(new EchoResponse
            {
                Message = request.Message
            });
        }

        public override Task<EchoResponse> EchoAbort(EchoRequest request, ServerCallContext context)
        {
            throw new RpcException(new Status(StatusCode.Aborted, "Aborted from server side."));
        }

        public override Task<Empty> NoOp(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new Empty());
        }

        public override async Task ServerStreamingEcho(ServerStreamingEchoRequest request, IServerStreamWriter<ServerStreamingEchoResponse> responseStream, ServerCallContext context)
        {
            for (var i = 0; i < request.MessageCount; i++)
            {
                await responseStream.WriteAsync(new ServerStreamingEchoResponse
                {
                    Message = request.Message
                });

                try
                {
                    await Task.Delay(request.MessageInterval.ToTimeSpan(), context.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        public override async Task ServerStreamingEchoAbort(ServerStreamingEchoRequest request, IServerStreamWriter<ServerStreamingEchoResponse> responseStream, ServerCallContext context)
        {
            await responseStream.WriteAsync(new ServerStreamingEchoResponse
            {
                Message = request.Message
            });

            throw new RpcException(new Status(StatusCode.Aborted, "Aborted from server side."));
        }

        public override async Task<ClientStreamingEchoResponse> ClientStreamingEcho(IAsyncStreamReader<ClientStreamingEchoRequest> requestStream, ServerCallContext context)
        {
            var i = 0;
            await foreach (var message in requestStream.ReadAllAsync())
            {
                i++;
            }

            return new ClientStreamingEchoResponse { MessageCount = i };
        }

        public override async Task FullDuplexEcho(IAsyncStreamReader<EchoRequest> requestStream, IServerStreamWriter<EchoResponse> responseStream, ServerCallContext context)
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(new EchoResponse
                {
                    Message = message.Message
                });
            }
        }

        public override async Task HalfDuplexEcho(IAsyncStreamReader<EchoRequest> requestStream, IServerStreamWriter<EchoResponse> responseStream, ServerCallContext context)
        {
            var messages = new List<string>();
            await foreach (var message in requestStream.ReadAllAsync())
            {
                messages.Add(message.Message);
            }

            foreach (var message in messages)
            {
                await responseStream.WriteAsync(new EchoResponse
                {
                    Message = message
                });
            }
        }
    }
}
