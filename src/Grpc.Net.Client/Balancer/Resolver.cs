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

#if HAVE_LOAD_BALANCING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Configuration;

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
    /// is responsible for eventually invoking <see cref="RefreshAsync(CancellationToken)"/>.
    /// </para>
    /// </summary>
    public abstract class Resolver : IDisposable
    {
        /// <summary>
        /// Starts resolution.
        /// </summary>
        /// <param name="listener">The callback used to receive updates on the target.</param>
        public abstract void Start(Action<ResolverResult> listener);

        /// <summary>
        /// Refresh resolution. Can only be called after <see cref="Start(Action{ResolverResult})"/>.
        /// <para>
        /// This is only a hint. Implementation takes it as a signal but may not start resolution.
        /// </para>
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A task.</returns>
        public abstract Task RefreshAsync(CancellationToken cancellationToken);

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
    /// </summary>
    public sealed class ResolverResult
    {
        private BalancerAttributes? _attributes;

        [DebuggerStepThrough]
        private ResolverResult(Status status, IReadOnlyList<DnsEndPoint>? addresses, ServiceConfig? serviceConfig)
        {
            Status = status;
            Addresses = addresses;
            ServiceConfig = serviceConfig;
        }

        /// <summary>
        /// Gets the status. A status other than <see cref="StatusCode.OK"/> indicates failure.
        /// </summary>
        public Status Status { get; }

        /// <summary>
        /// Gets a collection of resolved addresses.
        /// </summary>
        public IReadOnlyList<DnsEndPoint>? Addresses { get; }

        /// <summary>
        /// Gets an optional service config.
        /// </summary>
        public ServiceConfig? ServiceConfig { get; }

        /// <summary>
        /// Gets metadata attributes.
        /// </summary>
        public BalancerAttributes Attributes => _attributes ??= new BalancerAttributes();

        /// <summary>
        /// Create <see cref="ResolverResult"/> for error.
        /// </summary>
        /// <param name="status">The error status.</param>
        /// <returns>A resolver result.</returns>
        [DebuggerStepThrough]
        public static ResolverResult ForError(Status status)
        {
            if (status.StatusCode == StatusCode.OK)
            {
                throw new ArgumentException("Error status code must not be OK.", nameof(status));
            }

            return new ResolverResult(status, addresses: null, serviceConfig: null);
        }

        /// <summary>
        /// Create <see cref="ResolverResult"/> for the specified addresses.
        /// </summary>
        /// <param name="addresses">The resolved addresses.</param>
        /// <param name="serviceConfig">An optional service config.</param>
        /// <returns>A resolver result.</returns>
        [DebuggerStepThrough]
        public static ResolverResult ForResult(IReadOnlyList<DnsEndPoint> addresses, ServiceConfig? serviceConfig)
        {
            return new ResolverResult(Status.DefaultSuccess, addresses, serviceConfig);
        }
    }
}
#endif