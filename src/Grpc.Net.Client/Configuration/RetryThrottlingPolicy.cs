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
    /// The retry throttling policy for a server.
    /// <para>
    /// For more information about configuring throttling, see https://github.com/grpc/proposal/blob/master/A6-client-retries.md#throttling-retry-attempts-and-hedged-rpcs.
    /// </para>
    /// </summary>
    /// <remarks>
    /// <para>
    /// Represents the <c>RetryThrottlingPolicy</c> message in https://github.com/grpc/grpc-proto/blob/master/grpc/service_config/service_config.proto.
    /// </para>
    /// </remarks>
    public sealed class RetryThrottlingPolicy : ConfigObject
    {
        internal const string MaxTokensPropertyName = "maxTokens";
        internal const string TokenRatioPropertyName = "tokenRatio";

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryThrottlingPolicy"/> class.
        /// </summary>
        public RetryThrottlingPolicy() { }
        internal RetryThrottlingPolicy(IDictionary<string, object> inner) : base(inner) { }

        /// <summary>
        /// Gets or sets the maximum number of tokens.
        /// The number of tokens starts at <see cref="MaxTokens"/> and the token count will
        /// always be between 0 and <see cref="MaxTokens"/>.
        /// This property is required and must be greater than zero.
        /// </summary>
        public int? MaxTokens
        {
            get => GetValue<int>(MaxTokensPropertyName);
            set => SetValue(MaxTokensPropertyName, value);
        }

        /// <summary>
        /// Gets or sets the amount of tokens to add on each successful call. Typically this will
        /// be some number between 0 and 1, e.g., 0.1.
        /// This property is required and must be greater than zero. Up to 3 decimal places are supported.
        /// </summary>
        public double? TokenRatio
        {
            get => GetValue<double>(TokenRatioPropertyName);
            set => SetValue(TokenRatioPropertyName, value);
        }
    }
}
