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
using System.Collections.Generic;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class DefaultGrpcInterceptorActivator<TInterceptor> : IGrpcInterceptorActivator<TInterceptor> where TInterceptor : Interceptor
    {
        private readonly IServiceProvider _serviceProvider;
        private HashSet<Interceptor> _createdInterceptors;

        public DefaultGrpcInterceptorActivator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Interceptor Create(params object[] args)
        {
            if (args.Length == 0)
            {
                var globalInterceptor = _serviceProvider.GetService<TInterceptor>();
                if (globalInterceptor != null)
                {
                    return globalInterceptor;
                }
            }

            var interceptor = ActivatorUtilities.CreateInstance<TInterceptor>(_serviceProvider, args);
            
            if (_createdInterceptors == null)
            {
                _createdInterceptors = new HashSet<Interceptor>();
            }

            _createdInterceptors.Add(interceptor);

            return interceptor;
        }

        public void Release(Interceptor interceptor)
        {
            if (interceptor == null)
            {
                throw new ArgumentNullException(nameof(interceptor));
            }

            if (interceptor is IDisposable disposableInterceptor && _createdInterceptors != null && _createdInterceptors.Contains(interceptor))
            {
                _createdInterceptors.Remove(interceptor); 
                disposableInterceptor.Dispose();
            }
        }
    }
}
