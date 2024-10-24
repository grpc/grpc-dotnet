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

using Google.Protobuf;
using Grpc.Core;
using Grpc.Testing;

sealed class BenchmarkServiceImpl : BenchmarkService.BenchmarkServiceBase
{
#if CLIENT_CERTIFICATE_AUTHENTICATION
    [Microsoft.AspNetCore.Authorization.Authorize]
#endif
    public override Task<SimpleResponse> UnaryCall(SimpleRequest request, ServerCallContext context)
    {
        return Task.FromResult(CreateResponse(request));
    }

    public override async Task StreamingCall(IAsyncStreamReader<SimpleRequest> requestStream, IServerStreamWriter<SimpleResponse> responseStream, ServerCallContext context)
    {
        await foreach (var item in requestStream.ReadAllAsync())
        {
            await responseStream.WriteAsync(CreateResponse(item));
        }
    }

    public override async Task StreamingFromServer(SimpleRequest request, IServerStreamWriter<SimpleResponse> responseStream, ServerCallContext context)
    {
        var response = CreateResponse(request);
        while (!context.CancellationToken.IsCancellationRequested)
        {
            await responseStream.WriteAsync(response);
        }
    }

    public override async Task<SimpleResponse> StreamingFromClient(IAsyncStreamReader<SimpleRequest> requestStream, ServerCallContext context)
    {
        SimpleRequest? lastRequest = null;
        await foreach (var item in requestStream.ReadAllAsync())
        {
            lastRequest = item;
        };

        if (lastRequest == null)
        {
            throw new InvalidOperationException("No client requests received.");
        }

        return CreateResponse(lastRequest);
    }

    public override async Task StreamingBothWays(IAsyncStreamReader<SimpleRequest> requestStream, IServerStreamWriter<SimpleResponse> responseStream, ServerCallContext context)
    {
        var response = new SimpleResponse
        {
            Payload = new Payload { Body = UnsafeByteOperations.UnsafeWrap(new byte[100]) }
        };
        var clientComplete = false;

        var readTask = Task.Run(async () =>
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                // Nom nom nom
            }

            clientComplete = true;
        });

        // Write outgoing messages until client is complete
        while (!clientComplete)
        {
            await responseStream.WriteAsync(response);
        }

        await readTask;
    }

    public static SimpleResponse CreateResponse(SimpleRequest request)
    {
        var data = request.ResponseSize == 0 ? Array.Empty<byte>() : new byte[request.ResponseSize];
        var body = UnsafeByteOperations.UnsafeWrap(data);

        var payload = new Payload { Body = body };
        return new SimpleResponse { Payload = payload };
    }
}
