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
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Grpc.Shared.Server
{
    /// <summary>
    /// Unary server method invoker.
    /// </summary>
    /// <typeparam name="TService">Service type for this method.</typeparam>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    internal sealed class UnaryServerMethodInvoker<TService, TRequest, TResponse> : ServerMethodInvokerBase<TService, TRequest, TResponse>
        where TRequest : class
        where TResponse : class
        where TService : class
    {
        private readonly UnaryServerMethod<TService, TRequest, TResponse> _invoker;
        private readonly UnaryServerMethod<TRequest, TResponse>? _pipelineInvoker;

        /// <summary>
        /// Creates a new instance of <see cref="UnaryServerMethodInvoker{TService, TRequest, TResponse}"/>.
        /// </summary>
        /// <param name="invoker">The unary method to invoke.</param>
        /// <param name="method">The description of the gRPC method.</param>
        /// <param name="options">The options used to execute the method.</param>
        /// <param name="serviceActivator">The service activator used to create service instances.</param>
        public UnaryServerMethodInvoker(
            UnaryServerMethod<TService, TRequest, TResponse> invoker,
            Method<TRequest, TResponse> method,
            MethodOptions options,
            IGrpcServiceActivator<TService> serviceActivator)
            : base(method, options, serviceActivator)
        {
            _invoker = invoker;

            if (Options.HasInterceptors)
            {
                var interceptorPipeline = new InterceptorPipelineBuilder<TRequest, TResponse>(Options.Interceptors);
                _pipelineInvoker = interceptorPipeline.UnaryPipeline(ResolvedInterceptorInvoker);
            }
        }

        private async Task<TResponse> ResolvedInterceptorInvoker(TRequest resolvedRequest, ServerCallContext resolvedContext)
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
                    await ServiceActivator.ReleaseAsync(serviceHandle);
                }
            }
        }

        /// <summary>
        /// Invoke the unary method with the specified <see cref="HttpContext"/>.
        /// </summary>
        /// <param name="httpContext">The <see cref="HttpContext"/> for the current request.</param>
        /// <param name="serverCallContext">The <see cref="ServerCallContext"/>.</param>
        /// <param name="request">The <typeparamref name="TRequest"/> message.</param>
        /// <returns>A <see cref="Task{TResponse}"/> that represents the asynchronous method. The <see cref="Task{TResponse}.Result"/>
        /// property returns the <typeparamref name="TResponse"/> message.</returns>
        public Task<TResponse> Invoke(HttpContext httpContext, ServerCallContext serverCallContext, TRequest request)
        {
            if (_pipelineInvoker == null)
            {
                GrpcActivatorHandle<TService> serviceHandle = default;
                Task<TResponse>? invokerTask = null;
                try
                {
                    serviceHandle = ServiceActivator.Create(httpContext.RequestServices);
                    invokerTask = _invoker(
                        serviceHandle.Instance,
                        request,
                        serverCallContext);
                }
                catch (Exception ex)
                {
                    // Invoker calls user code and instead of returning a faulted task
                    // it may directly throw an exception. Catch and handle converting the
                    // exception into a task.
                    if (serviceHandle.Instance != null)
                    {
                        var releaseTask = ServiceActivator.ReleaseAsync(serviceHandle);
                        if (!releaseTask.IsCompletedSuccessfully)
                        {
                            return AwaitServiceReleaseAndThrow(serviceHandle, ex);
                        }
                    }

                    return Task.FromException<TResponse>(ex);
                }

                if (invokerTask.IsCompletedSuccessfully && serviceHandle.Instance != null)
                {
                    var releaseTask = ServiceActivator.ReleaseAsync(serviceHandle);
                    if (!releaseTask.IsCompletedSuccessfully)
                    {
                        return AwaitServiceReleaseAndReturn(serviceHandle, invokerTask.Result);
                    }

                    return invokerTask;
                }

                return AwaitInvoker(serviceHandle, invokerTask);
            }
            else
            {
                return _pipelineInvoker(
                    request,
                    serverCallContext);
            }
        }

        private async Task<TResponse> AwaitInvoker(GrpcActivatorHandle<TService> serviceHandle, Task<TResponse> invokerTask)
        {
            try
            {
                return await invokerTask;
            }
            finally
            {
                if (serviceHandle.Instance != null)
                {
                    await ServiceActivator.ReleaseAsync(serviceHandle);
                }
            }
        }

        private async Task<TResponse> AwaitServiceReleaseAndThrow(GrpcActivatorHandle<TService> serviceHandle, Exception ex)
        {
            await ServiceActivator.ReleaseAsync(serviceHandle);
            ExceptionDispatchInfo.Capture(ex).Throw();
            
            // Should never reach here
            return null;
        }

        private async Task<TResponse> AwaitServiceReleaseAndReturn(GrpcActivatorHandle<TService> serviceHandle, TResponse invokerResult)
        {
            await ServiceActivator.ReleaseAsync(serviceHandle);
            return invokerResult;
        }
    }
}
