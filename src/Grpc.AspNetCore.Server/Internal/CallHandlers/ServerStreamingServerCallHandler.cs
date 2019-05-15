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
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class ServerStreamingServerCallHandler<TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly ServerStreamingServerMethod<TService, TRequest, TResponse> _invoker;
        private static readonly Func<InterceptorRegistration, ServerStreamingServerMethod<TRequest, TResponse>, ServerStreamingServerMethod<TRequest, TResponse>> BuildInvoker = (interceptorRegistration, next) =>
        {
            return async (request, responseStream, context) =>
            {
                var interceptorActivator = (IGrpcInterceptorActivator)context.GetHttpContext().RequestServices.GetRequiredService(interceptorRegistration.ActivatorType);
                var interceptorInstance = interceptorActivator.Create(interceptorRegistration.Args);

                if (interceptorInstance == null)
                {
                    throw new InvalidOperationException($"Could not construct Interceptor instance for type {interceptorRegistration.Type.FullName}");
                }

                try
                {
                    await interceptorInstance.ServerStreamingServerHandler(request, responseStream, context, next);
                }
                finally
                {
                    interceptorActivator.Release(interceptorInstance);
                }
            };
        };

        public ServerStreamingServerCallHandler(
            Method<TRequest, TResponse> method, 
            ServerStreamingServerMethod<TService, TRequest, TResponse> invoker, 
            GrpcServiceOptions serviceOptions, 
            ILoggerFactory loggerFactory) 
            : base(method, serviceOptions, loggerFactory)
        {
            _invoker = invoker;
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext)
        {
            var serverCallContext = CreateServerCallContext(httpContext);

            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

            var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
            TService service = null;

            try
            {
                serverCallContext.Initialize();

                // Decode request
                var requestPayload = await httpContext.Request.BodyReader.ReadSingleMessageAsync(serverCallContext);
                var request = Method.RequestMarshaller.Deserializer(requestPayload);


                if (ServiceOptions.Interceptors.IsEmpty)
                {
                    try
                    {
                        service = activator.Create();
                        await _invoker(
                            service,
                            request,
                            new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.Serializer),
                            serverCallContext);
                    }
                    finally
                    {
                        if (service != null)
                        {
                            activator.Release(service);
                        }
                    }
                }
                else
                {
                    ServerStreamingServerMethod<TRequest, TResponse> resolvedInvoker = async (request, responseStream, resolvedContext) =>
                    {
                        try
                        {
                            service = activator.Create();
                            await _invoker(
                                service,
                                request,
                                responseStream,
                                resolvedContext);
                        }
                        finally
                        {
                            if (service != null)
                            {
                                activator.Release(service);
                            }
                        }
                    };

                    // The list is reversed during construction so the first interceptor is built last and invoked first
                    for (var i = ServiceOptions.Interceptors.Count - 1; i >= 0; i--)
                    {
                        resolvedInvoker = BuildInvoker(ServiceOptions.Interceptors[i], resolvedInvoker);
                    }

                    await resolvedInvoker(
                        request,
                        new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.Serializer),
                        serverCallContext);
                }
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
            await httpContext.Response.BodyWriter.FlushAsync();
        }
    }
}
