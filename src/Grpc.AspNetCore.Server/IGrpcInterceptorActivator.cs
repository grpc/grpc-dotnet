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

using Grpc.Core.Interceptors;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// An interceptor activator abstraction.
    /// </summary>
    public interface IGrpcInterceptorActivator
    {
        /// <summary>
        /// Creates an interceptor.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <param name="interceptorRegistration">The arguments to pass to the interceptor type instance's constructor.</param>
        /// <returns>The created interceptor.</returns>
        GrpcActivatorHandle<Interceptor> Create(IServiceProvider serviceProvider, InterceptorRegistration interceptorRegistration);

        /// <summary>
        /// Releases the specified interceptor.
        /// </summary>
        /// <param name="interceptor">The interceptor to release.</param>
        ValueTask ReleaseAsync(GrpcActivatorHandle<Interceptor> interceptor);
    }
}
