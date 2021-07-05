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
using System.Diagnostics;
using Grpc.Core;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Represents the balancer state.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class BalancerState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BalancerState"/> class with the specified state.
        /// </summary>
        /// <param name="connectivityState">The connectivity state.</param>
        /// <param name="picker">The subchannel picker.</param>
        [DebuggerStepThrough]
        public BalancerState(ConnectivityState connectivityState, SubchannelPicker picker)
        {
            ConnectivityState = connectivityState;
            Picker = picker;
        }

        /// <summary>
        /// Gets the connectivity state.
        /// </summary>
        public ConnectivityState ConnectivityState { get; }

        /// <summary>
        /// Gets the subchannel picker.
        /// </summary>
        public SubchannelPicker Picker { get; }
}
}
#endif
