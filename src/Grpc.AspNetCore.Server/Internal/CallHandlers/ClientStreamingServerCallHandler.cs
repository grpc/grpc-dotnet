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
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class ClientStreamingServerCallHandler<TRequest, TResponse, TService> : ServerCallHandlerBase<TRequest, TResponse, TService>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        // We're using an open delegate (the first argument is the TService instance) to represent the call here since the instance is create per request.
        // This is the reason we're not using the delegates defined in Grpc.Core. This delegate maps to ClientStreamingServerMethod<TRequest, TResponse>
        // with an instance parameter.
        private delegate Task<TResponse> ClientStreamingServerMethod(TService service, IAsyncStreamReader<TRequest> stream, ServerCallContext serverCallContext);

        private readonly ClientStreamingServerMethod _invoker;

        public ClientStreamingServerCallHandler(Method<TRequest, TResponse> method, GrpcServiceOptions serviceOptions, ILoggerFactory loggerFactory) : base(method, serviceOptions, loggerFactory)
        {
            var handlerMethod = typeof(TService).GetMethod(Method.Name);

            _invoker = (ClientStreamingServerMethod)Delegate.CreateDelegate(typeof(ClientStreamingServerMethod), handlerMethod);
        }

        public override async Task HandleCallAsync(HttpContext httpContext)
        {
            httpContext.Response.ContentType = "application/grpc";
            httpContext.Response.Headers.Append("grpc-encoding", "identity");

            var serverCallContext = new HttpContextServerCallContext(httpContext, ServiceOptions, Logger);

            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            TService service = null;

            TResponse response = null;
            try
            {
                serverCallContext.Initialize();

                service = activator.Create();

                response = await _invoker(
                    service,
                    new HttpContextStreamReader<TRequest>(httpContext, serverCallContext, Method.RequestMarshaller.Deserializer),
                    serverCallContext);

                if (response == null)
                {
                    // This is consistent with Grpc.Core when a null value is returned
                    throw new RpcException(new Status(StatusCode.Cancelled, "Cancelled"));
                }

                var responseBodyPipe = httpContext.Response.BodyPipe;
                await responseBodyPipe.WriteMessageAsync(response, serverCallContext, Method.ResponseMarshaller.Serializer);
            }
            catch (Exception ex)
            {
                serverCallContext.ProcessHandlerError(ex, Method.Name);
            }
            finally
            {
                serverCallContext.Dispose();
                if (service != null)
                {
                    activator.Release(service);
                }
            }

            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Flush any buffered content
            await httpContext.Response.BodyPipe.FlushAsync();
        }
    }
}
