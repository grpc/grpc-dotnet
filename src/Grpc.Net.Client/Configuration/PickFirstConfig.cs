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

using System.Collections.Generic;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// Configuration for pick_first load balancer policy.
    /// </summary>
    public sealed class PickFirstConfig : LoadBalancingConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PickFirstConfig"/> class.
        /// </summary>
        public PickFirstConfig() : base(PickFirstPolicyName)
        {
        }

        internal PickFirstConfig(IDictionary<string, object> inner) : base(inner) { }
    }
}
