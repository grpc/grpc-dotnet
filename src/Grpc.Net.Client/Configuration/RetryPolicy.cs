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
using Grpc.Core;
using Grpc.Net.Client.Internal.Configuration;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// The retry policy for outgoing calls.
    /// </summary>
    public sealed class RetryPolicy : ConfigObject
    {
        internal const string MaxAttemptsPropertyName = "maxAttempts";
        internal const string InitialBackoffPropertyName = "initialBackoff";
        internal const string MaxBackoffPropertyName = "maxBackoff";
        internal const string BackoffMultiplierPropertyName = "backoffMultiplier";
        internal const string RetryableStatusCodesPropertyName = "retryableStatusCodes";

        private ConfigProperty<Values<StatusCode, object>, IList<object>> _retryableStatusCodes =
            new(i => new Values<StatusCode, object>(i ?? new List<object>(), s => ConvertHelpers.ConvertStatusCode(s), s => ConvertHelpers.ConvertStatusCode(s.ToString()!)), RetryableStatusCodesPropertyName);

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryPolicy"/> class.
        /// </summary>
        public RetryPolicy() { }
        internal RetryPolicy(IDictionary<string, object> inner) : base(inner) { }

        /// <summary>
        /// Gets or sets the maximum number of call attempts. This value includes the original attempt.
        /// This property is required and must be greater than 1.
        /// This value is limited by <see cref="GrpcChannelOptions.MaxRetryAttempts"/>.
        /// </summary>
        public int? MaxAttempts
        {
            get => GetValue<int>(MaxAttemptsPropertyName);
            set => SetValue(MaxAttemptsPropertyName, value);
        }

        /// <summary>
        /// Gets or sets the initial backoff.
        /// A randomized delay between 0 and the current backoff value will determine when the next
        /// retry attempt is made.
        /// This property is required and must be greater than zero.
        /// <para>
        /// The backoff will be multiplied by <see cref="BackoffMultiplier"/> after each retry
        /// attempt and will increase exponentially when the multiplier is greater than 1.
        /// </para>
        /// </summary>
        public TimeSpan? InitialBackoff
        {
            get => ConvertHelpers.ConvertDurationText(GetValue<string>(InitialBackoffPropertyName));
            set => SetValue(InitialBackoffPropertyName, ConvertHelpers.ToDurationText(value));
        }

        /// <summary>
        /// Gets or sets the maximum backoff.
        /// The maximum backoff places an upper limit on exponential backoff growth.
        /// This property is required and must be greater than zero.
        /// </summary>
        public TimeSpan? MaxBackoff
        {
            get => ConvertHelpers.ConvertDurationText(GetValue<string>(MaxBackoffPropertyName));
            set => SetValue(MaxBackoffPropertyName, ConvertHelpers.ToDurationText(value));
        }

        /// <summary>
        /// Gets or sets the backoff multiplier.
        /// The backoff will be multiplied by <see cref="BackoffMultiplier"/> after each retry
        /// attempt and will increase exponentially when the multiplier is greater than 1.
        /// This property is required and must be greater than 0.
        /// </summary>
        public double? BackoffMultiplier
        {
            get => GetValue<double>(BackoffMultiplierPropertyName);
            set => SetValue(BackoffMultiplierPropertyName, value);
        }

        /// <summary>
        /// Gets a collection of status codes which may be retried.
        /// At least one status code is required.
        /// </summary>
        public IList<StatusCode> RetryableStatusCodes
        {
            get => _retryableStatusCodes.GetValue(this)!;
        }
    }
}
