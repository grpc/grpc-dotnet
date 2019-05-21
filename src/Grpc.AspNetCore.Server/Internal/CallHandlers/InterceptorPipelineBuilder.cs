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
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal.CallHandlers
{
    internal class InterceptorPipelineBuilder<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly InterceptorCollection _interceptors;

        public InterceptorPipelineBuilder(InterceptorCollection interceptors)
        {
            _interceptors = interceptors;
        }

        public ClientStreamingServerMethod<TRequest, TResponse> ClientStreamingPipeline(ClientStreamingServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static ClientStreamingServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, ClientStreamingServerMethod<TRequest, TResponse> next)
            {
                return async (requestStream, context) =>
                {
                    var interceptorActivator = (IGrpcInterceptorActivator)context.GetHttpContext().RequestServices.GetRequiredService(interceptorRegistration.ActivatorType);
                    var interceptorInstance = CreateInterceptor(interceptorRegistration, interceptorActivator);

                    try
                    {
                        return await interceptorInstance.ClientStreamingServerHandler(requestStream, context, next);
                    }
                    finally
                    {
                        interceptorActivator.Release(interceptorInstance);
                    }
                };
            }
        }

        internal DuplexStreamingServerMethod<TRequest, TResponse> DuplexStreamingPipeline(DuplexStreamingServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static DuplexStreamingServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, DuplexStreamingServerMethod<TRequest, TResponse> next)
            {
                return async (requestStream, responseStream, context) =>
                {
                    var interceptorActivator = (IGrpcInterceptorActivator)context.GetHttpContext().RequestServices.GetRequiredService(interceptorRegistration.ActivatorType);
                    var interceptorInstance = CreateInterceptor(interceptorRegistration, interceptorActivator);

                    try
                    {
                        await interceptorInstance.DuplexStreamingServerHandler(requestStream, responseStream, context, next);
                    }
                    finally
                    {
                        interceptorActivator.Release(interceptorInstance);
                    }
                };
            }
        }

        internal ServerStreamingServerMethod<TRequest, TResponse> ServerStreamingPipeline(ServerStreamingServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static ServerStreamingServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, ServerStreamingServerMethod<TRequest, TResponse> next)
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
            }
        }

        internal UnaryServerMethod<TRequest, TResponse> UnaryPipeline(UnaryServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static UnaryServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, UnaryServerMethod<TRequest, TResponse> next)
            {
                return async (request, context) =>
                {
                    var interceptorActivator = (IGrpcInterceptorActivator)context.GetHttpContext().RequestServices.GetRequiredService(interceptorRegistration.ActivatorType);
                    var interceptorInstance = CreateInterceptor(interceptorRegistration, interceptorActivator);

                    try
                    {
                        return await interceptorInstance.UnaryServerHandler(request, context, next);
                    }
                    finally
                    {
                        interceptorActivator.Release(interceptorInstance);
                    }
                };
            }
        }

        private T BuildPipeline<T>(T innerInvoker, Func<InterceptorRegistration, T, T> wrapInvoker)
        {
            // The inner invoker will create the service instance and invoke the method
            var resolvedInvoker = innerInvoker;

            // The list is reversed during construction so the first interceptor is built last and invoked first
            for (var i = _interceptors.Count - 1; i >= 0; i--)
            {
                resolvedInvoker = wrapInvoker(_interceptors[i], resolvedInvoker);
            }

            return resolvedInvoker;
        }

        private static Interceptor CreateInterceptor(InterceptorRegistration interceptorRegistration, IGrpcInterceptorActivator interceptorActivator)
        {
            var interceptorInstance = interceptorActivator.Create(interceptorRegistration.Args);

            if (interceptorInstance == null)
            {
                throw new InvalidOperationException($"Could not construct Interceptor instance for type {interceptorRegistration.Type.FullName}");
            }

            return interceptorInstance;
        }
    }
}