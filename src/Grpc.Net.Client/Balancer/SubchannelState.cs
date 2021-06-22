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
    /// Represents subchannel state.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class SubchannelState
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SubchannelState"/> class.
        /// </summary>
        /// <param name="state">The connectivity state.</param>
        /// <param name="status">The status.</param>
        [DebuggerStepThrough]
        internal SubchannelState(ConnectivityState state, Status status)
        {
            State = state;
            Status = status;
        }

        /// <summary>
        /// Gets the connectivity state.
        /// </summary>
        public ConnectivityState State { get; }

        /// <summary>
        /// Gets the status.
        /// </summary>
        public Status Status { get; }
    }
}
#endif
