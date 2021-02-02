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
        private const string RetryThrottlingPropertyName = "retryThrottling";

        private ConfigProperty<Values<MethodConfig, object>, IList<object>> _methods =
            new(i => new Values<MethodConfig, object>(i ?? new List<object>(), s => s.Inner, s => new MethodConfig((IDictionary<string, object>)s)), MethodConfigPropertyName);

        private ConfigProperty<RetryThrottlingPolicy, IDictionary<string, object>> _retryThrottling =
            new(i => i != null ? new RetryThrottlingPolicy(i) : null, RetryThrottlingPropertyName);

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceConfig"/> class.
        /// </summary>
        public ServiceConfig() { }
        internal ServiceConfig(IDictionary<string, object> inner) : base(inner) { }

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
