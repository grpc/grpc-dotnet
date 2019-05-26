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
            ILoggerFactory loggerFactory)
            : base(method, serviceOptions, loggerFactory)
        {
            _invoker = invoker;

            if (!ServiceOptions.Interceptors.IsEmpty)
            {
                DuplexStreamingServerMethod<TRequest, TResponse> resolvedInvoker = async (requestStream, responseStream, resolvedContext) =>
                {
                    var activator = resolvedContext.GetHttpContext().RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
                    TService? service = null;
                    try
                    {
                        service = activator.Create();
                        await _invoker(
                            service,
                            requestStream,
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

                var interceptorPipeline = new InterceptorPipelineBuilder<TRequest, TResponse>(ServiceOptions.Interceptors);
                _pipelineInvoker = interceptorPipeline.DuplexStreamingPipeline(resolvedInvoker);
            }
        }

        protected override async Task HandleCallAsyncCore(HttpContext httpContext)
        {
            // Disable request body data rate for client streaming
            DisableMinRequestBodyDataRate(httpContext);

            var serverCallContext = CreateServerCallContext(httpContext);

            GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

            try
            {
                serverCallContext.Initialize();

                if (_pipelineInvoker == null)
                {
                    var activator = httpContext.RequestServices.GetRequiredService<IGrpcServiceActivator<TService>>();
                    TService? service = null;
                    try
                    {
                        service = activator.Create();
                        await _invoker(
                            service,
                            new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.Deserializer),
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
                    await _pipelineInvoker(
                        new HttpContextStreamReader<TRequest>(serverCallContext, Method.RequestMarshaller.Deserializer),
                        new HttpContextStreamWriter<TResponse>(serverCallContext, Method.ResponseMarshaller.Serializer),
                        serverCallContext);
                }

                await serverCallContext.EndCallAsync();
            }
            catch (Exception ex)
            {
                serverCallContext.ProcessHandlerError(ex, Method.Name);
            }
            finally
            {
                serverCallContext.Dispose();
            }
        }
    }
}
