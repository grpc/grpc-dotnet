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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// A <see cref="Resolver"/> that returns a static collection of addresses.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    internal sealed class StaticResolver : Resolver
    {
        private readonly List<BalancerAddress> _addresses;

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticResolver"/> class with the specified addresses.
        /// </summary>
        /// <param name="addresses">The resolved addresses.</param>
        /// <param name="loggerFactory">The logger factory.</param>
        public StaticResolver(IEnumerable<BalancerAddress> addresses, ILoggerFactory loggerFactory)
            : base(loggerFactory)
        {
            _addresses = addresses.ToList();
        }

        protected override void OnStarted()
        {
            // Send addresses to listener once. They will never change.
            Listener(ResolverResult.ForResult(_addresses, serviceConfig: null, serviceConfigStatus: null));
        }

        /// <inheritdoc />
        protected override Task ResolveAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A <see cref="ResolverFactory"/> that matches the URI scheme <c>static</c>
    /// and creates <see cref="StaticResolver"/> instances.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class StaticResolverFactory : ResolverFactory
    {
        private readonly Func<Uri, IEnumerable<BalancerAddress>> _addressesCallback;

        /// <summary>
        /// Initializes a new instance of the <see cref="StaticResolverFactory"/> class with a callback
        /// that returns a collection of addresses for a target <see cref="Uri"/>.
        /// </summary>
        /// <param name="addressesCallback">
        /// A callback that returns a collection of addresses for a target <see cref="Uri"/>.
        /// </param>
        public StaticResolverFactory(Func<Uri, IEnumerable<BalancerAddress>> addressesCallback)
        {
            _addressesCallback = addressesCallback;
        }

        /// <inheritdoc />
        public override string Name => "static";

        /// <inheritdoc />
        public override Resolver Create(ResolverOptions options)
        {
            return new StaticResolver(_addressesCallback(options.Address), options.LoggerFactory);
        }
    }
}
#endif
