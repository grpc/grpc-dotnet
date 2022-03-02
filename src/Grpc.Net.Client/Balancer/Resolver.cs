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
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// A configurable component that resolves a target <see cref="Uri"/> and returns them to the caller.
    /// The targets URI's scheme is used to select the <see cref="Resolver"/> implementation, and uses the
    /// URI parts after the scheme for actual resolution.
    /// <para>
    /// The addresses of a target may change over time, thus the caller registers a callback to receive
    /// continuous updates as <see cref="ResolverResult"/>.
    /// </para>
    /// <para>
    /// A <see cref="Resolver"/> doesn't need to automatically re-resolve on failure. Instead, the callback
    /// is responsible for eventually invoking <see cref="Refresh()"/>.
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class Resolver : IDisposable
    {
        /// <summary>
        /// Starts listening to resolver for results with the specified callback. Can only be called once.
        /// <para>
        /// The <see cref="ResolverResult"/> passed to the callback has addresses when successful,
        /// otherwise a <see cref="Status"/> details the resolution error.
        /// </para>
        /// </summary>
        /// <param name="listener">The callback used to receive updates on the target.</param>
        public abstract void Start(Action<ResolverResult> listener);

        /// <summary>
        /// Refresh resolution. Can only be called after <see cref="Start(Action{ResolverResult})"/>.
        /// The default implementation is no-op.
        /// <para>
        /// This is only a hint. Implementation takes it as a signal but may not start resolution.
        /// </para>
        /// </summary>
        public virtual void Refresh()
        {
            // no-op
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="LoadBalancer"/> and optionally releases
        /// the managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Disposes the <see cref="Resolver"/>. Stops resolution.
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents the results from a <see cref="Resolver"/>.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class ResolverResult
    {
        private BalancerAttributes? _attributes;

        [DebuggerStepThrough]
        private ResolverResult(Status status, IReadOnlyList<BalancerAddress>? addresses, ServiceConfig? serviceConfig, Status? serviceConfigStatus)
        {
            Status = status;
            Addresses = addresses;
            ServiceConfig = serviceConfig;
            ServiceConfigStatus = serviceConfigStatus;
        }

        /// <summary>
        /// Gets the status. A status other than <see cref="StatusCode.OK"/> indicates failure.
        /// </summary>
        public Status Status { get; }

        /// <summary>
        /// Gets a collection of resolved addresses.
        /// </summary>
        public IReadOnlyList<BalancerAddress>? Addresses { get; }

        /// <summary>
        /// Gets an optional service config.
        /// </summary>
        public ServiceConfig? ServiceConfig { get; }

        /// <summary>
        /// Gets an optional service config status.
        /// </summary>
        public Status? ServiceConfigStatus { get; }

        /// <summary>
        /// Gets metadata attributes.
        /// </summary>
        public BalancerAttributes Attributes => _attributes ??= new BalancerAttributes();

        /// <summary>
        /// Create <see cref="ResolverResult"/> for failure.
        /// </summary>
        /// <param name="status">The error status. Must not be <see cref="StatusCode.OK"/>.</param>
        /// <returns>A resolver result.</returns>
        [DebuggerStepThrough]
        public static ResolverResult ForFailure(Status status)
        {
            if (status.StatusCode == StatusCode.OK)
            {
                throw new ArgumentException("Error status code must not be OK.", nameof(status));
            }

            return new ResolverResult(status, addresses: null, serviceConfig: null, serviceConfigStatus: null);
        }

        /// <summary>
        /// Create <see cref="ResolverResult"/> for the specified addresses.
        /// </summary>
        /// <param name="addresses">The resolved addresses.</param>
        /// <returns>A resolver result.</returns>
        [DebuggerStepThrough]
        public static ResolverResult ForResult(IReadOnlyList<BalancerAddress> addresses)
        {
            return new ResolverResult(Status.DefaultSuccess, addresses, serviceConfig: null, serviceConfigStatus: null);
        }

        /// <summary>
        /// Create <see cref="ResolverResult"/> for the specified addresses and service config.
        /// </summary>
        /// <param name="addresses">The resolved addresses.</param>
        /// <param name="serviceConfig">An optional service config. A <c>null</c> value indicates that the resolver either didn't retreive a service config or an error occurred. The error must be specified using <paramref name="serviceConfigStatus"/>.</param>
        /// <param name="serviceConfigStatus">A service config status. The status indicates an error retreiveing or parsing the config. The status must not be <see cref="StatusCode.OK"/> if no service config is specified.</param>
        /// <returns>A resolver result.</returns>
        [DebuggerStepThrough]
        public static ResolverResult ForResult(IReadOnlyList<BalancerAddress> addresses, ServiceConfig? serviceConfig, Status? serviceConfigStatus)
        {
            if (serviceConfigStatus?.StatusCode == StatusCode.OK && serviceConfig == null)
            {
                throw new ArgumentException("Service config status code must not be OK when there is no service config.", nameof(serviceConfigStatus));
            }

            return new ResolverResult(Status.DefaultSuccess, addresses, serviceConfig, serviceConfigStatus);
        }
    }
}
#endif
