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

#if SUPPORT_LOAD_BALANCING
using System;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Factory for creating new <see cref="Resolver"/> instances. A factory is used when the
    /// target address <see cref="Uri"/> scheme matches the factory name.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class ResolverFactory
    {
        /// <summary>
        /// Gets the resolver factory name. A factory is used when the target <see cref="Uri"/> scheme
        /// matches the factory name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Creates a new <see cref="Resolver"/> with the specified options.
        /// </summary>
        /// <param name="address">The target address <see cref="Uri"/>.</param>
        /// <param name="options">The options.</param>
        /// <returns>A new <see cref="Resolver"/>.</returns>
        public abstract Resolver Create(Uri address, ResolverOptions options);
    }

    /// <summary>
    /// Options for creating a resolver.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class ResolverOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ResolverOptions"/> class.
        /// </summary>
        /// <param name="disableServiceConfig">
        /// The flag indicating whether the resolver should disable resolving a service config.
        /// </param>
        internal ResolverOptions(bool disableServiceConfig)
        {
            DisableServiceConfig = disableServiceConfig;
        }

        /// <summary>
        /// Gets a flag indicating whether the resolver should disable resolving a service config.
        /// </summary>
        public bool DisableServiceConfig { get; }
    }
}
#endif
