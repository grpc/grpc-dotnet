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
            MethodContext methodContext,
            ILoggerFactory loggerFactory,
            IGrpcServiceActivator serviceActivator,
            IServiceProvider serviceProvider)
            : base(method, methodContext, loggerFactory, serviceActivator, serviceProvider)
        {
            _invoker = invoker;

            if (MethodContext.HasInterceptors)
            {
                var interceptorPipeline = new InterceptorPipelineBuilder<TRequest, TResponse>(MethodContext.Interceptors, ServiceProvider);
                _pipelineInvoker = interceptorPipeline.DuplexStreamingPipeline(ResolvedInterceptorInvoker);
            }
        }

        private async Task ResolvedInterceptorInvoker(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext resolvedContext)
        {
            var service = (TService)ServiceActivator.Create(resolvedContext, typeof(TService));

            try
            {
                await _invoker(
                    service,
                    requestStream,
                    responseStream,
                    resolvedContext);
            }
            finally
            {
                await ServiceActivator.ReleaseAsync(service);
            }
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext, HttpContextServerCallContext serverCallContext)
        {
            // Disable request body data rate for client streaming
            DisableMinRequestBodyDataRateAndMaxRequestBodySize(httpContext);

            if (_pipelineInvoker == null)
            {
                var service = (TService)ServiceActivator.Create(serverCallContext, typeof(TService));
                try
                {
                    await _invoker(
                        service,
                        new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.ContextualDeserializer),
                        new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.ContextualSerializer),
                        serverCallContext);
                }
                finally
                {
                    await ServiceActivator.ReleaseAsync(service);
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
