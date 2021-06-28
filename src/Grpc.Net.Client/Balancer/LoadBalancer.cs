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
using Grpc.Core;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// A configurable component that receives resolved addresses from <see cref="Resolver"/> and provides a usable
    /// <see cref="Subchannel"/> when asked.
    /// <para>
    /// A new load balancer implements:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="LoadBalancerFactory"/> creates new <see cref="LoadBalancer"/> instances.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="LoadBalancer"/> receives results from the <see cref="Resolver"/>, subchannels'
    ///       connectivity states, and requests to create a connection and shutdown.
    ///       <see cref="LoadBalancer"/> creates <see cref="Subchannel"/> instances from resolve results using
    ///       <see cref="IChannelControlHelper"/>, and updates the channel state with a <see cref="ConnectivityState"/>
    ///       and <see cref="SubchannelPicker"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="SubchannelPicker"/> makes a load balancing decision.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class LoadBalancer : IDisposable
    {
        /// <summary>
        /// Updates the <see cref="LoadBalancer"/> with state from the <see cref="Resolver"/>.
        /// </summary>
        /// <param name="state">State from the <see cref="Resolver"/>.</param>
        public abstract void UpdateChannelState(ChannelState state);

        /// <summary>
        /// Request the <see cref="LoadBalancer"/> to establish connections now (if applicable) so that
        /// future calls can use a ready connection without waiting for a connection.
        /// </summary>
        public abstract void RequestConnection();

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
        /// Disposes the <see cref="LoadBalancer"/>.
        /// The load balancer state is updated to <see cref="ConnectivityState.Shutdown"/>.
        /// state 
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Factory for creating new <see cref="LoadBalancer"/> instances. A factory is used when the load balancer config name
    /// matches the factory name.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public abstract class LoadBalancerFactory
    {
#if SUPPORT_LOAD_BALANCING
        internal static readonly LoadBalancerFactory[] KnownLoadBalancerFactories = new LoadBalancerFactory[]
        {
            new PickFirstBalancerFactory(),
            new RoundRobinBalancerFactory()
        };
#endif

        /// <summary>
        /// Gets the load balancer factory name. A factory is used when the load balancer config name
        /// matches the factory name.
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Creates a new <see cref="LoadBalancer"/> with the specified options.
        /// </summary>
        /// <param name="options">Options for creating a <see cref="LoadBalancer"/>.</param>
        /// <returns>A new <see cref="LoadBalancer"/>.</returns>
        public abstract LoadBalancer Create(LoadBalancerOptions options);
    }
}
#endif
