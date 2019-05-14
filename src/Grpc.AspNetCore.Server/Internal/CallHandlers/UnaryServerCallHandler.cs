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

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class UnaryServerCallHandler<TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly UnaryServerMethod<TService, TRequest, TResponse> _invoker;
        private static readonly Func<InterceptorRegistration, UnaryServerMethod<TRequest, TResponse>, UnaryServerMethod<TRequest, TResponse>> BuildInvoker = (interceptorRegistration, next) =>
        {
            return async (request, context) =>
            {
                var interceptorActivator = (IGrpcInterceptorActivator)context.GetHttpContext().RequestServices.GetRequiredService(interceptorRegistration.ActivatorType);
                var interceptorInstance = interceptorActivator.Create(interceptorRegistration.Args);

                if (interceptorInstance == null)
                {
                    throw new InvalidOperationException($"Could not construct Interceptor instance for type {interceptorRegistration.Type.FullName}");
                }

                try
                {
                    return await interceptorInstance.UnaryServerHandler(request, context, next);
                }
                finally
                {
                    interceptorActivator.Release(interceptorInstance);
                }
            };
        };

        public UnaryServerCallHandler(
            Method<TRequest, TResponse> method, 
            UnaryServerMethod<TService, TRequest, TResponse> invoker, 
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
            TResponse response = null;

            try
            {
                serverCallContext.Initialize();
                var requestPayload = await httpContext.Request.BodyReader.ReadSingleMessageAsync(serverCallContext);
                var request = Method.RequestMarshaller.Deserializer(requestPayload);

                if (ServiceOptions.Interceptors.IsEmpty)
                {
                    try
                    {
                        service = activator.Create();
                        response = await _invoker(service, request, serverCallContext);
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
                    UnaryServerMethod<TRequest, TResponse> resolvedInvoker = async (resolvedRequest, resolvedContext) =>
                    {
                        try
                        {
                            service = activator.Create();
                            return await _invoker(service, resolvedRequest, resolvedContext);
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

                    response = await resolvedInvoker(request, serverCallContext);
                }

                if (response == null)
                {
                    // This is consistent with Grpc.Core when a null value is returned
                    throw new RpcException(new Status(StatusCode.Cancelled, "No message returned from method."));
                }

                var responseBodyWriter = httpContext.Response.BodyWriter;
                await responseBodyWriter.WriteMessageAsync(response, serverCallContext, Method.ResponseMarshaller.Serializer);
            }
            catch (Exception ex)
            {
                serverCallContext.ProcessHandlerError(ex, Method.Name);
            }
            finally
            {
                serverCallContext.Dispose();
            }

            httpContext.Response.ConsolidateTrailers(serverCallContext);

            // Flush any buffered content
            await httpContext.Response.BodyWriter.FlushAsync();
        }
    }
}
