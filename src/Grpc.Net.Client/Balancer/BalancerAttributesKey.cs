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

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Represents the key used to get and set <see cref="BalancerAttributes"/> values.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    public readonly struct BalancerAttributesKey<TValue>
    {
        /// <summary>
        /// Gets the key.
        /// </summary>
        public string Key { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="BalancerAttributesKey{TValue}"/> struct with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        [DebuggerStepThrough]
        public BalancerAttributesKey(string key)
        {
            Key = key;
        }
    }
}
#endif
