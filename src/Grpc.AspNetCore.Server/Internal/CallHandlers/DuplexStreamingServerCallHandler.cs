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
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
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
        private readonly DuplexStreamingServerMethod<TRequest, TResponse>? _pipelineInvoker;

        public DuplexStreamingServerCallHandler(
            Method<TRequest, TResponse> method,
            DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker,
            GrpcServiceOptions serviceOptions,
            ILoggerFactory loggerFactory,
            IGrpcServiceActivator<TService> serviceActivator,
            IServiceProvider serviceProvider)
            : base(method, serviceOptions, loggerFactory, serviceActivator, serviceProvider)
        {
            _invoker = invoker;

            if (ServiceOptions.HasInterceptors)
            {
                DuplexStreamingServerMethod<TRequest, TResponse> resolvedInvoker = async (requestStream, responseStream, resolvedContext) =>
                {
                    GrpcActivatorHandle<TService> serviceHandle = default;
                    try
                    {
                        serviceHandle = ServiceActivator.Create(resolvedContext.GetHttpContext().RequestServices);
                        await _invoker(
                            serviceHandle.Instance,
                            requestStream,
                            responseStream,
                            resolvedContext);
                    }
                    finally
                    {
                        if (serviceHandle.Instance != null)
                        {
                            ServiceActivator.Release(serviceHandle);
                        }
                    }
                };

                var interceptorPipeline = new InterceptorPipelineBuilder<TRequest, TResponse>(ServiceOptions.Interceptors, ServiceProvider);
                _pipelineInvoker = interceptorPipeline.DuplexStreamingPipeline(resolvedInvoker);
            }
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
        {
            // Disable request body data rate for client streaming
            DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);

            if (_pipelineInvoker == null)
            {
                GrpcActivatorHandle<TService> serviceHandle = default;
                try
                {
                    serviceHandle = ServiceActivator.Create(httpContext.RequestServices);
                    await _invoker(
                        serviceHandle.Instance,
                        new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.ContextualDeserializer),
                        new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.ContextualSerializer),
                        serverCallContext);
                }
                finally
                {
                    if (serviceHandle.Instance != null)
                    {
                        ServiceActivator.Release(serviceHandle);
                    }
                }
            }
            else
            {
                await _pipelineInvoker(
                    new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.ContextualDeserializer),
                    new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.ContextualSerializer),
                    serverCallContext);
            }
        }
    }
}
