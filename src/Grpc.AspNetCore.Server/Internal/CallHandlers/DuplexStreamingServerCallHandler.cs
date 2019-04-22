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
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class DuplexStreamingServerCallHandler<TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly DuplexStreamingServerMethod<TService, TRequest, TResponse> _invoker;
        private static readonly Func<Interceptor, DuplexStreamingServerMethod<TRequest, TResponse>, DuplexStreamingServerMethod<TRequest, TResponse>> BuildInvoker = (interceptor, next) =>
        {
            return (requestStream, responseStream, context) =>
            {
                return interceptor.DuplexStreamingServerHandler(requestStream, responseStream, context, next);
            };
        };

        public DuplexStreamingServerCallHandler(
            Method<TRequest, TResponse> method, 
            DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker, 
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

                service = activator.Create();

                if (ServiceOptions.Interceptors.IsEmpty)
                {
                    await _invoker(
                        service,
                        new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.Deserializer),
                        new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.Serializer),
                        serverCallContext);
                }
                else
                {
                    DuplexStreamingServerMethod<TRequest, TResponse> resolvedInvoker = (requestStream, responseStream, resolvedContext) =>
                    {
                        return _invoker(
                        service,
                        requestStream,
                        responseStream,
                        resolvedContext);
                    };

                    // The list is reversed during construction so the first interceptor is invoked first
                    for (var i = ServiceOptions.Interceptors.Count - 1; i >= 0; i--)
                    {
                        var interceptor = CreateInterceptor(ServiceOptions.Interceptors[i], httpContext.RequestServices);
                        resolvedInvoker = BuildInvoker(interceptor, resolvedInvoker);
                    }

                    await resolvedInvoker(
                        new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.Deserializer),
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
