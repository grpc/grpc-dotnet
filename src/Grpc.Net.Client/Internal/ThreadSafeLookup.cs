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

using System.Collections.Concurrent;

internal sealed class ThreadSafeLookup<TKey, TValue> where TKey : notnull
{
    // Avoid allocating ConcurrentDictionary until the threshold is reached.
    // Looking up a key in an array is as fast as a dictionary for small collections and uses much less memory.
    private const int Threshold = 10;

    private KeyValuePair<TKey, TValue>[] _array = Array.Empty<KeyValuePair<TKey, TValue>>();
    private ConcurrentDictionary<TKey, TValue>? _dictionary;

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        if (_dictionary != null)
        {
            return _dictionary.GetOrAdd(key, valueFactory);
        }

        var snapshot = _array;
        foreach (var kvp in snapshot)
        {
            if (EqualityComparer<TKey>.Default.Equals(kvp.Key, key))
            {
                return kvp.Value;
            }
        }

        var newValue = valueFactory(key);

        if (snapshot.Length + 1 > Threshold)
        {
            // Lock here to ensure that only one thread will create the initial dictionary.
            lock (this)
            {
                if (_dictionary != null)
                {
                    _dictionary.TryAdd(key, newValue);
                }
                else
                {
                    var newDict = new ConcurrentDictionary<TKey, TValue>();
                    foreach (var kvp in snapshot)
                    {
                        newDict.TryAdd(kvp.Key, kvp.Value);
                    }
                    newDict.TryAdd(key, newValue);

                    _dictionary = newDict;
                    _array = Array.Empty<KeyValuePair<TKey, TValue>>();
                }
            }
        }
        else
        {
            var newArray = new KeyValuePair<TKey, TValue>[snapshot.Length + 1];
            Array.Copy(snapshot, newArray, snapshot.Length);
            newArray[newArray.Length - 1] = new KeyValuePair<TKey, TValue>(key, newValue);

            _array = newArray;
        }

        return newValue;
    }
}
