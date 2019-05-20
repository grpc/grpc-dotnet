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
using System.Linq;
using Grpc.Core.Interceptors;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Represents the pipeline of interceptors to be invoked when processing a gRPC call.
    /// </summary>
    public class InterceptorCollection : IReadOnlyList<InterceptorRegistration>
    {
        private static readonly IEnumerator<InterceptorRegistration> EmptyEnumerator = Enumerable.Empty<InterceptorRegistration>().GetEnumerator();

        private List<InterceptorRegistration>? _store;

        /// <summary>
        /// Get whether the collection contains any interceptors.
        /// </summary>
        public bool IsEmpty => _store == null || _store.Count == 0;

        /// <inheritdoc />
        public int Count => _store?.Count ?? 0;

        /// <inheritdoc />
        public InterceptorRegistration this[int index]
        {
            get
            {
                if (_store == null)
                {
                    throw new IndexOutOfRangeException();
                }

                return _store[index];
            }
        }

        /// <summary>
        /// Add an interceptor to the end of the pipeline.
        /// </summary>
        /// <typeparam name="TInterceptor">The interceptor type.</typeparam>
        /// <param name="args">The list of arguments to pass to the interceptor constructor when creating an instance.</param>
        public void Add<TInterceptor>(params object[] args) where TInterceptor : Interceptor
        {
            if (_store == null)
            {
                _store = new List<InterceptorRegistration>();
            }

            _store.Add(new InterceptorRegistration(typeof(TInterceptor), args));
        }

        /// <summary>
        /// Append a set of interceptors to the end of the pipeline.
        /// </summary>
        /// <param name="collection">The set of interceptors to add.</param>
        public void AddRange(InterceptorCollection collection)
        {
            if (collection.IsEmpty)
            {
                return;
            }

            if (_store == null)
            {
                _store = new List<InterceptorRegistration>();
            }

            _store.AddRange(collection);
        }

        /// <inheritdoc />
        public IEnumerator<InterceptorRegistration> GetEnumerator() => _store?.GetEnumerator() ?? EmptyEnumerator;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
