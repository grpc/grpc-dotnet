﻿#region Copyright notice and license

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
using Grpc.Core;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class ClientStreamingServerCallHandler<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)]
#endif
        TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly ClientStreamingServerMethodInvoker<TService, TRequest, TResponse> _invoker;

        public ClientStreamingServerCallHandler(
            ClientStreamingServerMethodInvoker<TService, TRequest, TResponse> invoker,
            ILoggerFactory loggerFactory)
            : base(invoker, loggerFactory)
        {
            _invoker = invoker;
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
        {
            // Disable request body data rate for client streaming
            DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);

            TResponse? response;

            var streamReader = new HttpContextStreamReader<TRequest>(serverCallContext, MethodInvoker.Method.RequestMarshaller.ContextualDeserializer);
            try
            {
                response = await _invoker.Invoke(httpContext, serverCallContext, streamReader);
            }
            finally
            {
                streamReader.Complete();
            }

            if (response == null)
            {
                // This is consistent with Grpc.Core when a null value is returned
                throw new RpcException(new Status(StatusCode.Cancelled, "No message returned from method."));
            }

            // Check if deadline exceeded while method was invoked. If it has then skip trying to write
            // the response message because it will always fail.
            // Note that the call is still going so the deadline could still be exceeded after this point.
            if (serverCallContext.DeadlineManager?.IsDeadlineExceededStarted ?? false)
            {
                return;
            }

            var responseBodyWriter = httpContext.Response.BodyWriter;
            await responseBodyWriter.WriteMessageAsync(response, serverCallContext, MethodInvoker.Method.ResponseMarshaller.ContextualSerializer, canFlush: false);
        }
    }
}
