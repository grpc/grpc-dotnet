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
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace Grpc.Net.Client.Balancer
{
    /// <summary>
    /// Collection of load balancing metadata attributes.
    /// <para>
    /// Note: Experimental API that can change or be removed without any prior notice.
    /// </para>
    /// </summary>
    public sealed class BalancerAttributes : IDictionary<string, object?>
    {
        /// <summary>
        /// Gets a read-only collection of metadata attributes.
        /// </summary>
        public static readonly BalancerAttributes Empty = new BalancerAttributes(new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>()));

        private readonly IDictionary<string, object?> _attributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="BalancerAttributes"/> class.
        /// </summary>
        public BalancerAttributes() : this(new Dictionary<string, object?>())
        {
        }

        private BalancerAttributes(IDictionary<string, object?> attributes)
        {
            _attributes = attributes;
        }

        object? IDictionary<string, object?>.this[string key]
        {
            get
            {
                return _attributes[key];
            }
            set
            {
                _attributes[key] = value;
            }
        }
        ICollection<string> IDictionary<string, object?>.Keys => _attributes.Keys;
        ICollection<object?> IDictionary<string, object?>.Values => _attributes.Values;
        int ICollection<KeyValuePair<string, object?>>.Count => _attributes.Count;
        bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => ((IDictionary<string, object?>)_attributes).IsReadOnly;
        void IDictionary<string, object?>.Add(string key, object? value) => _attributes.Add(key, value);
        void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_attributes).Add(item);
        void ICollection<KeyValuePair<string, object?>>.Clear() => _attributes.Clear();
        bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_attributes).Contains(item);
        bool IDictionary<string, object?>.ContainsKey(string key) => _attributes.ContainsKey(key);
        void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
            ((IDictionary<string, object?>)_attributes).CopyTo(array, arrayIndex);
        IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator() => _attributes.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((System.Collections.IEnumerable)_attributes).GetEnumerator();
        bool IDictionary<string, object?>.Remove(string key) => _attributes.Remove(key);
        bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item) => ((IDictionary<string, object?>)_attributes).Remove(item);
        bool IDictionary<string, object?>.TryGetValue(string key, out object? value) => _attributes.TryGetValue(key, out value);

        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <typeparam name="TValue">The value type.</typeparam>
        /// <param name="key">The key of the <see cref="BalancerAttributesKey{TValue}"/> to get.</param>
        /// <param name="value">
        /// When this method returns, contains the value associated with the specified key, if the key is found
        /// and the value type matches the specified type. Otherwise, contains the default value for the type of
        /// the <c>value</c> parameter.
        /// </param>
        /// <returns>
        /// <c>true</c> if the <see cref="BalancerAttributes"/> contains an element with the specified key and value type; otherwise <c>false</c>.
        /// </returns>
        public bool TryGetValue<TValue>(BalancerAttributesKey<TValue> key, [MaybeNullWhen(false)] out TValue value)
        {
            if (_attributes.TryGetValue(key.Key, out object? o) && o is TValue v)
            {
                value = v;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Sets the value associated with the specified key.
        /// </summary>
        /// <typeparam name="TValue">The value type.</typeparam>
        /// <param name="key">The key of the value to set.</param>
        /// <param name="value">The value.</param>
        public void Set<TValue>(BalancerAttributesKey<TValue> key, TValue value)
        {
            _attributes[key.Key] = value;
        }
    }
}
#endif
