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

using System.Diagnostics.CodeAnalysis;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers;

internal sealed class DuplexStreamingServerCallHandler<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
    where TRequest : class
    where TResponse : class
    where TService : class
{
    private readonly DuplexStreamingServerMethodInvoker<TService, TRequest, TResponse> _invoker;

    public DuplexStreamingServerCallHandler(
        DuplexStreamingServerMethodInvoker<TService, TRequest, TResponse> invoker,
        ILoggerFactory loggerFactory)
        : base(invoker, loggerFactory)
    {
        _invoker = invoker;
    }

    protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
    {
        // Disable certain features for duplex streaming methods.
        DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);
#if NET8_0_OR_GREATER
        DisableRequestTimeout(httpContext);
#endif

        var streamReader = new HttpContextStreamReader<TRequest>(serverCallContext, MethodInvoker.Method.RequestMarshaller.ContextualDeserializer);
        var streamWriter = new HttpContextStreamWriter<TResponse>(serverCallContext, MethodInvoker.Method.ResponseMarshaller.ContextualSerializer);
        try
        {
            await _invoker.Invoke(httpContext, serverCallContext, streamReader, streamWriter);
        }
        finally
        {
            streamReader.Complete();
            streamWriter.Complete();
        }
    }
}
