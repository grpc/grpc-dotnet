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
        private readonly IServiceProvider _serviceProvider;

        public InterceptorPipelineBuilder(InterceptorCollection interceptors, IServiceProvider serviceProvider)
        {
            _interceptors = interceptors;
            _serviceProvider = serviceProvider;
        }

        public ClientStreamingServerMethod<TRequest, TResponse> ClientStreamingPipeline(ClientStreamingServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static ClientStreamingServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, IServiceProvider serviceProvider, ClientStreamingServerMethod<TRequest, TResponse> next)
            {
                return async (requestStream, context) =>
                {
                    var interceptorActivator = interceptorRegistration.GetActivator(serviceProvider);
                    var interceptorHandle = CreateInterceptor(interceptorRegistration, interceptorActivator, context.GetHttpContext().RequestServices);

                    try
                    {
                        return await interceptorHandle.Instance.ClientStreamingServerHandler(requestStream, context, next);
                    }
                    finally
                    {
                        await interceptorActivator.ReleaseAsync(interceptorHandle);
                    }
                };
            }
        }

        internal DuplexStreamingServerMethod<TRequest, TResponse> DuplexStreamingPipeline(DuplexStreamingServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static DuplexStreamingServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, IServiceProvider serviceProvider, DuplexStreamingServerMethod<TRequest, TResponse> next)
            {
                return async (requestStream, responseStream, context) =>
                {
                    var interceptorActivator = interceptorRegistration.GetActivator(serviceProvider);
                    var interceptorHandle = CreateInterceptor(interceptorRegistration, interceptorActivator, context.GetHttpContext().RequestServices);

                    try
                    {
                        await interceptorHandle.Instance.DuplexStreamingServerHandler(requestStream, responseStream, context, next);
                    }
                    finally
                    {
                        await interceptorActivator.ReleaseAsync(interceptorHandle);
                    }
                };
            }
        }

        internal ServerStreamingServerMethod<TRequest, TResponse> ServerStreamingPipeline(ServerStreamingServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static ServerStreamingServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, IServiceProvider serviceProvider, ServerStreamingServerMethod<TRequest, TResponse> next)
            {
                return async (request, responseStream, context) =>
                {
                    var interceptorActivator = interceptorRegistration.GetActivator(serviceProvider);
                    var interceptorHandle = interceptorActivator.Create(context.GetHttpContext().RequestServices, interceptorRegistration);

                    if (interceptorHandle.Instance == null)
                    {
                        throw new InvalidOperationException($"Could not construct Interceptor instance for type {interceptorRegistration.Type.FullName}");
                    }

                    try
                    {
                        await interceptorHandle.Instance.ServerStreamingServerHandler(request, responseStream, context, next);
                    }
                    finally
                    {
                        await interceptorActivator.ReleaseAsync(interceptorHandle);
                    }
                };
            }
        }

        internal UnaryServerMethod<TRequest, TResponse> UnaryPipeline(UnaryServerMethod<TRequest, TResponse> innerInvoker)
        {
            return BuildPipeline(innerInvoker, BuildInvoker);

            static UnaryServerMethod<TRequest, TResponse> BuildInvoker(InterceptorRegistration interceptorRegistration, IServiceProvider serviceProvider, UnaryServerMethod<TRequest, TResponse> next)
            {
                return async (request, context) =>
                {
                    var interceptorActivator = interceptorRegistration.GetActivator(serviceProvider);
                    var interceptorHandle = CreateInterceptor(interceptorRegistration, interceptorActivator, context.GetHttpContext().RequestServices);

                    try
                    {
                        return await interceptorHandle.Instance.UnaryServerHandler(request, context, next);
                    }
                    finally
                    {
                        await interceptorActivator.ReleaseAsync(interceptorHandle);
                    }
                };
            }
        }

        private T BuildPipeline<T>(T innerInvoker, Func<InterceptorRegistration, IServiceProvider, T, T> wrapInvoker)
        {
            // The inner invoker will create the service instance and invoke the method
            var resolvedInvoker = innerInvoker;

            // The list is reversed during construction so the first interceptor is built last and invoked first
            for (var i = _interceptors.Count - 1; i >= 0; i--)
            {
                resolvedInvoker = wrapInvoker(_interceptors[i], _serviceProvider, resolvedInvoker);
            }

            return resolvedInvoker;
        }

        private static GrpcActivatorHandle<Interceptor> CreateInterceptor(InterceptorRegistration interceptorRegistration, IGrpcInterceptorActivator interceptorActivator, IServiceProvider serviceProvider)
        {
            var interceptorHandle = interceptorActivator.Create(serviceProvider, interceptorRegistration);

            if (interceptorHandle.Instance == null)
            {
                throw new InvalidOperationException($"Could not construct Interceptor instance for type {interceptorRegistration.Type.FullName}");
            }

            return interceptorHandle;
        }
    }
}