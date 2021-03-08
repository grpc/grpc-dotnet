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
using System.Collections;
using System.Collections.Generic;

namespace Grpc.Net.Client.Internal.Configuration
{
    internal class Values<T, TInner> : IList<T>, IConfigValue
    {
        internal readonly IList<TInner> Inner;

        private readonly IList<T> _values;
        internal readonly Func<T, TInner> _convertTo;
        internal readonly Func<TInner, T> _convertFrom;

        public Values(IList<TInner> inner, Func<T, TInner> convertTo, Func<TInner, T> convertFrom)
        {
            Inner = inner;
            _values = new List<T>();
            _convertTo = convertTo;
            _convertFrom = convertFrom;

            foreach (var item in Inner)
            {
                _values.Add(_convertFrom(item));
            }
        }

        public T this[int index]
        {
            get => _values[index];
            set
            {
                _values[index] = value;
                Inner[index] = _convertTo(value);
            }
        }

        public int Count => Inner.Count;
        public bool IsReadOnly => Inner.IsReadOnly;

        object IConfigValue.Inner => Inner;

        public void Add(T item)
        {
            _values.Add(item);
            Inner.Add(_convertTo(item));
        }

        public void Clear()
        {
            _values.Clear();
            Inner.Clear();
        }

        public bool Contains(T item) => _values.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _values.CopyTo(array, arrayIndex);

        public IEnumerator<T> GetEnumerator() => _values.GetEnumerator();

        public int IndexOf(T item) => _values.IndexOf(item);

        public void Insert(int index, T item)
        {
            _values.Insert(index, item);
            Inner.Insert(index, _convertTo(item));
        }

        public bool Remove(T item) => _values.Remove(item) && Inner.Remove(_convertTo(item));

        public void RemoveAt(int index)
        {
            _values.RemoveAt(index);
            Inner.RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
