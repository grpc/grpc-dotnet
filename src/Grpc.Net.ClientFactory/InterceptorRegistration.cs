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
using Grpc.Core.Interceptors;

namespace Grpc.Net.ClientFactory
{
    /// <summary>
    /// Representation of a registration of the interceptor in the client pipeline.
    /// </summary>
    public class InterceptorRegistration
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InterceptorRegistration"/> class.
        /// </summary>
        /// <param name="lifetime">The lifetime of the interceptor.</param>
        /// <param name="creator">A delegate that is used to create an <see cref="Interceptor"/>.</param>
        public InterceptorRegistration(InterceptorLifetime lifetime, Func<IServiceProvider, Interceptor> creator)
        {
            Lifetime = lifetime;
            Creator = creator;
        }

        /// <summary>
        /// Gets the lifetime of the interceptor.
        /// </summary>
        public InterceptorLifetime Lifetime { get; }

        /// <summary>
        /// Gets a delegate that is used to create an <see cref="Interceptor"/>.
        /// </summary>
        public Func<IServiceProvider, Interceptor> Creator { get; }
    }
}
