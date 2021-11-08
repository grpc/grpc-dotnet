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
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Represents options used to create <see cref="Subchannel"/>.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class SubchannelOptions
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SubchannelOptions"/> class.
        /// </summary>
        /// <param name="addresses">A collection of addresses.</param>
        [DebuggerStepThrough]
        public SubchannelOptions(IReadOnlyList<BalancerAddress> addresses)
        {
            Addresses = addresses;
        }

        /// <summary>
        /// Gets a collection of addresses.
        /// </summary>
        public IReadOnlyList<BalancerAddress> Addresses { get; }
    }
}
#endif
