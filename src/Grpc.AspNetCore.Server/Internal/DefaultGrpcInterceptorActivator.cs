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
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class DefaultGrpcInterceptorActivator<TInterceptor> : IGrpcInterceptorActivator<TInterceptor> where TInterceptor : Interceptor
    {
        public GrpcActivatorHandle<Interceptor> Create(IServiceProvider serviceProvider, InterceptorRegistration interceptorRegistration)
        {
            if (interceptorRegistration.Arguments.Count == 0)
            {
                var globalInterceptor = serviceProvider.GetService<TInterceptor>();
                if (globalInterceptor != null)
                {
                    return new GrpcActivatorHandle<Interceptor>(globalInterceptor, created: false, state: null);
                }
            }

            // Cache factory on registration
            var factory = interceptorRegistration.GetFactory();

            var interceptor = (TInterceptor)factory(serviceProvider, interceptorRegistration._args);

            return new GrpcActivatorHandle<Interceptor>(interceptor, created: true, state: null);
        }

        public void Release(in GrpcActivatorHandle<Interceptor> interceptor)
        {
            if (interceptor.Instance == null)
            {
                throw new ArgumentException("Interceptor instance is null.", nameof(interceptor));
            }

            if (interceptor.Created && interceptor.Instance is IDisposable disposableInterceptor)
            {
                disposableInterceptor.Dispose();
            }
        }
    }
}
