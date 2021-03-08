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

using System.Collections;
using System.Collections.Generic;
using Grpc.Net.Client.Internal.Configuration;

namespace Grpc.Net.Client.Configuration
{
    /// <summary>
    /// Represents a configuration object. Implementations provide strongly typed wrappers over
    /// collections of untyped values.
    /// </summary>
    public abstract class ConfigObject : IConfigValue
    {
        /// <summary>
        /// Gets the underlying configuration values.
        /// </summary>
        public IDictionary<string, object> Inner { get; }

        internal ConfigObject() : this(new Dictionary<string, object>())
        {
        }

        internal ConfigObject(IDictionary<string, object> inner)
        {
            Inner = inner;
        }

        object IConfigValue.Inner => Inner;

        internal T? GetValue<T>(string key)
        {
            if (Inner.TryGetValue(key, out var value))
            {
                return (T?)value;
            }
            return default;
        }

        internal void SetValue<T>(string key, T? value)
        {
            if (value == null)
            {
                Inner.Remove(key);
            }
            else
            {
                Inner[key] = value;
            }
        }
    }
}
