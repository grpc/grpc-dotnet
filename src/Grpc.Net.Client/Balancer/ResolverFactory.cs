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
using System.Threading;
using Microsoft.Extensions.Logging;

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
#if SUPPORT_LOAD_BALANCING
        internal static readonly ResolverFactory[] KnownLoadResolverFactories = new ResolverFactory[]
        {
            new DnsResolverFactory(Timeout.InfiniteTimeSpan)
        };
#endif

        /// <summary>
        /// Gets the resolver factory name. A factory is used when the target <see cref="Uri"/> scheme
        /// matches the factory name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Creates a new <see cref="Resolver"/> with the specified options.
        /// </summary>
        /// <param name="options">Options for creating a <see cref="Resolver"/>.</param>
        /// <returns>A new <see cref="Resolver"/>.</returns>
        public abstract Resolver Create(ResolverOptions options);
    }
}
#endif
