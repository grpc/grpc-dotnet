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
using System.Linq;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// Base type for load balancer policy configuration.
    /// </summary>
    public class LoadBalancingConfig : ConfigObject
    {
        // The policy names here match property names in in the service_config.proto
        // https://github.com/grpc/grpc-proto/blob/ec886024c2f7b7f597ba89d5b7d60c3f94627b17/grpc/service_config/service_config.proto#L328-L373

        /// <summary>
        /// <c>pick_first</c> policy name.
        /// </summary>
        public const string PickFirstPolicyName = "pick_first";

        /// <summary>
        /// <c>round_robin</c> policy name.
        /// </summary>
        public const string RoundRobinPolicyName = "round_robin";

        /// <summary>
        /// Initializes a new instance of the <see cref="LoadBalancingConfig"/> class.
        /// </summary>
        public LoadBalancingConfig(string loadBalancingPolicyName)
        {
            Inner[loadBalancingPolicyName] = new Dictionary<string, object>();
        }

        internal LoadBalancingConfig(IDictionary<string, object> inner) : base(inner) { }

        /// <summary>
        /// Gets the load balancer policy name.
        /// </summary>
        public string PolicyName => Inner.Keys.Single();
    }
}
