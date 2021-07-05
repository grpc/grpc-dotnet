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
using Grpc.Core;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Provides essentials for <see cref="LoadBalancer"/> implementations.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public interface IChannelControlHelper
    {
        /// <summary>
        /// Creates a <see cref="Subchannel"/>, which is a logical connection to the specified addresses.
        /// The <see cref="LoadBalancer"/> is responsible for closing unused subchannels, and closing
        /// all subchannels on shutdown.
        /// </summary>
        /// <param name="options">The options for the new <see cref="Subchannel"/>.</param>
        /// <returns>A new <see cref="Subchannel"/>.</returns>
        Subchannel CreateSubchannel(SubchannelOptions options);

        /// <summary>
        /// Update the balancing state. This includes a new <see cref="ConnectivityState"/> and
        /// <see cref="SubchannelPicker"/>. The state is used by currently queued and future calls.
        /// </summary>
        /// <param name="state">The balancer state.</param>
        void UpdateState(BalancerState state);

        /// <summary>
        /// Request the configured <see cref="Resolver"/> to refresh.
        /// </summary>
        void RefreshResolver();
    }
}
#endif
