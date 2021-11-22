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

using Grpc.Net.Client.Internal.Configuration;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// Configuration for a method.
    /// The <see cref="Names"/> collection is used to determine which methods this configuration applies to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Represents the <c>MethodConfig</c> message in https://github.com/grpc/grpc-proto/blob/master/grpc/service_config/service_config.proto.
    /// </para>
    /// </remarks>
    public sealed class MethodConfig : ConfigObject
    {
        private const string NamePropertyName = "name";
        private const string RetryPolicyPropertyName = "retryPolicy";
        private const string HedgingPolicyPropertyName = "hedgingPolicy";

        private ConfigProperty<Values<MethodName, object>, IList<object>> _names =
            new(i => new Values<MethodName, object>(i ?? new List<object>(), s => s.Inner, s => new MethodName((IDictionary<string, object>)s)), NamePropertyName);

        private ConfigProperty<RetryPolicy, IDictionary<string, object>> _retryPolicy =
            new(i => i != null ? new RetryPolicy(i) : null, RetryPolicyPropertyName);

        private ConfigProperty<HedgingPolicy, IDictionary<string, object>> _hedgingPolicy =
            new(i => i != null ? new HedgingPolicy(i) : null, HedgingPolicyPropertyName);

        /// <summary>
        /// Initializes a new instance of the <see cref="MethodConfig"/> class.
        /// </summary>
        public MethodConfig() { }
        internal MethodConfig(IDictionary<string, object> inner) : base(inner) { }

        /// <summary>
        /// Gets or sets the retry policy for outgoing calls.
        /// A retry policy can't be combined with <see cref="HedgingPolicy"/>.
        /// </summary>
        public RetryPolicy? RetryPolicy
        {
            get => _retryPolicy.GetValue(this);
            set => _retryPolicy.SetValue(this, value);
        }

        /// <summary>
        /// Gets or sets the hedging policy for outgoing calls. Hedged calls may execute
        /// more than once on the server, so only idempotent methods should specify a hedging
        /// policy. A hedging policy can't be combined with <see cref="RetryPolicy"/>.
        /// </summary>
        public HedgingPolicy? HedgingPolicy
        {
            get => _hedgingPolicy.GetValue(this);
            set => _hedgingPolicy.SetValue(this, value);
        }

        /// <summary>
        /// Gets a collection of names which determine the calls the method config will apply to.
        /// A <see cref="MethodConfig"/> without names won't be used. Each name must be unique
        /// across an entire <see cref="ServiceConfig"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// If a name's <see cref="MethodName.Method"/> property isn't set then the method config is the default
        /// for all methods for the specified service.
        /// </para>
        /// <para>
        /// If a name's <see cref="MethodName.Service"/> property isn't set then <see cref="MethodName.Method"/> must also be unset,
        /// and the method config is the default for all methods on all services.
        /// <see cref="MethodName.Default"/> represents this global default name.
        /// </para>
        /// <para>
        /// When determining which method config to use for a given RPC, the most specific match wins. A method config
        /// with a configured <see cref="MethodName"/> that exactly matches a call's method and service will be used
        /// instead of a service or global default method config.
        /// </para>
        /// </remarks>
        public IList<MethodName> Names
        {
            get => _names.GetValue(this)!;
        }
    }
}
