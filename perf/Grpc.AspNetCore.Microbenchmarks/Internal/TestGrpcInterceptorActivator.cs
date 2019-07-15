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
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core.Interceptors;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    internal class TestGrpcInterceptorActivator<TInterceptor> : IGrpcInterceptorActivator<TInterceptor> where TInterceptor : Interceptor
    {
        public readonly TInterceptor _interceptor;

        public TestGrpcInterceptorActivator(TInterceptor service)
        {
            _interceptor = service;
        }

        public GrpcActivatorHandle<Interceptor> Create(IServiceProvider serviceProvider, InterceptorRegistration interceptorRegistration)
        {
            return new GrpcActivatorHandle<Interceptor>(_interceptor, created: false, state: null);
        }

        public void Release(in GrpcActivatorHandle<Interceptor> interceptor) { }
    }
}
