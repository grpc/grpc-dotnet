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

using System;
using System.Collections.Generic;
using System.Linq;
using Grpc.Net.Client.Internal.Configuration;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// A <see cref="ServiceConfig"/> represents information about a service.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Represents the <c>ServiceConfig</c> message in https://github.com/grpc/grpc-proto/blob/master/grpc/service_config/service_config.proto.
    /// </para>
    /// </remarks>
    public sealed class ServiceConfig : ConfigObject
    {
        private const string MethodConfigPropertyName = "methodConfig";
        private const string LoadBalancingConfigPropertyName = "loadBalancingConfig";
        private const string RetryThrottlingPropertyName = "retryThrottling";

        private ConfigProperty<Values<MethodConfig, object>, IList<object>> _methods =
            new(i => new Values<MethodConfig, object>(i ?? new List<object>(), s => s.Inner, s => new MethodConfig((IDictionary<string, object>)s)), MethodConfigPropertyName);

        private ConfigProperty<Values<LoadBalancingConfig, object>, IList<object>> _loadBalancingConfigs =
            new(i => new Values<LoadBalancingConfig, object>(i ?? new List<object>(), s => s.Inner, s => CreateLoadBalanacingConfig((IDictionary<string, object>)s)), LoadBalancingConfigPropertyName);

        private static LoadBalancingConfig CreateLoadBalanacingConfig(IDictionary<string, object> s)
        {
            if (s.Count == 1)
            {
                var item = s.Single();
                switch (item.Key)
                {
                    case "round_robin":
                        return new RoundRobinConfig(s);
                    case "pick_first":
                        return new PickFirstConfig(s);
                    default:
                        // Unknown/unsupported config. Use base type.
                        return new LoadBalancingConfig(s);
                }
            }

            throw new InvalidOperationException("Unexpected error when parsing load balancing config.");
        }

        private ConfigProperty<RetryThrottlingPolicy, IDictionary<string, object>> _retryThrottling =
            new(i => i != null ? new RetryThrottlingPolicy(i) : null, RetryThrottlingPropertyName);

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceConfig"/> class.
        /// </summary>
        public ServiceConfig() { }
        internal ServiceConfig(IDictionary<string, object> inner) : base(inner) { }

        // Multiple LB policies can be specified; clients will iterate through
        // the list in order and stop at the first policy that they support. If none
        // are supported, the service config is considered invalid.

        /// <summary>
        /// Gets a collection of <see cref="LoadBalancingConfig"/> instances. The client will iterate
        /// through the configured policies in order and use the first policy that is supported.
        /// If none are supported by the client then a configuration error is thrown.
        /// </summary>
        public IList<LoadBalancingConfig> LoadBalancingConfigs
        {
            get => _loadBalancingConfigs.GetValue(this)!;
        }

        /// <summary>
        /// Gets a collection of <see cref="MethodConfig"/> instances. This collection is used to specify
        /// configuration on a per-method basis. <see cref="MethodConfig.Names"/> determines which calls
        /// a method config applies to.
        /// </summary>
        public IList<MethodConfig> MethodConfigs
        {
            get => _methods.GetValue(this)!;
        }

        /// <summary>
        /// Gets or sets the retry throttling policy.
        /// If a <see cref="RetryThrottlingPolicy"/> is provided, gRPC will automatically throttle
        /// retry attempts and hedged RPCs when the client's ratio of failures to
        /// successes exceeds a threshold.
        /// <para>
        /// For more information about configuring throttling, see https://github.com/grpc/proposal/blob/master/A6-client-retries.md#throttling-retry-attempts-and-hedged-rpcs.
        /// </para>
        /// </summary>
        public RetryThrottlingPolicy? RetryThrottling
        {
            get => _retryThrottling.GetValue(this);
            set => _retryThrottling.SetValue(this, value);
        }
    }
}
