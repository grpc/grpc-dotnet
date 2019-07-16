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
    internal class UnaryServerCallHandler<TService, TRequest, TResponse> : ServerCallHandlerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly UnaryServerMethod<TService, TRequest, TResponse> _invoker;
        private readonly UnaryServerMethod<TRequest, TResponse>? _pipelineInvoker;

        public UnaryServerCallHandler(
            Method<TRequest, TResponse> method,
            UnaryServerMethod<TService, TRequest, TResponse> invoker,
            GrpcServiceOptions serviceOptions,
            ILoggerFactory loggerFactory,
            IGrpcServiceActivator<TService> serviceActivator,
            IServiceProvider serviceProvider)
            : base(method, serviceOptions, loggerFactory, serviceActivator, serviceProvider)
        {
            _invoker = invoker;

            if (ServiceOptions.HasInterceptors)
            {
                UnaryServerMethod<TRequest, TResponse> resolvedInvoker = async (resolvedRequest, resolvedContext) =>
                {
                    GrpcActivatorHandle<TService> serviceHandle = default;
                    try
                    {
                        serviceHandle = ServiceActivator.Create(resolvedContext.GetHttpContext().RequestServices);
                        return await _invoker(serviceHandle.Instance, resolvedRequest, resolvedContext);
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
                _pipelineInvoker = interceptorPipeline.UnaryPipeline(resolvedInvoker);
            }
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
        {
            var requestPayload = await httpContext.Request.BodyReader.ReadSingleMessageAsync(serverCallContext);

            serverCallContext.DeserializationContext.SetPayload(requestPayload);
            var request = Method.RequestMarshaller.ContextualDeserializer(serverCallContext.DeserializationContext);
            serverCallContext.DeserializationContext.SetPayload(null);

            GrpcEventSource.Log.MessageReceived();

            TResponse? response = null;

            if (_pipelineInvoker == null)
            {
                GrpcActivatorHandle<TService> serviceHandle = default;
                try
                {
                    serviceHandle = ServiceActivator.Create(httpContext.RequestServices);
                    response = await _invoker(serviceHandle.Instance, request, serverCallContext);
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
                response = await _pipelineInvoker(request, serverCallContext);
            }

            if (response == null)
            {
                // This is consistent with Grpc.Core when a null value is returned
                throw new RpcException(new Status(StatusCode.Cancelled, "No message returned from method."));
            }

            var responseBodyWriter = httpContext.Response.BodyWriter;
            await responseBodyWriter.WriteMessageAsync(response, serverCallContext, Method.ResponseMarshaller.ContextualSerializer, canFlush: false);

            GrpcEventSource.Log.MessageSent();
        }
    }
}
