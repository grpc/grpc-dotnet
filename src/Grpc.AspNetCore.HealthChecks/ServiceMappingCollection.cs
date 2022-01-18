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
using System.Collections.ObjectModel;

namespace Grpc.AspNetCore.HealthChecks
{
    /// <summary>
    /// A collection of <see cref="ServiceMapping"/> used to map health results to gRPC health checks services.
    /// </summary>
    public sealed class ServiceMappingCollection : IEnumerable<ServiceMapping>
    {
        private sealed class ServiceMappingKeyedCollection : KeyedCollection<string, ServiceMapping>
        {
            protected override string GetKeyForItem(ServiceMapping item)
            {
                return item.Name;
            }
        }

        private readonly ServiceMappingKeyedCollection _mappings = new ServiceMappingKeyedCollection();

        /// <summary>
        /// Remove all service mappings.
        /// </summary>
        public void Clear() => _mappings.Clear();

        /// <summary>
        /// Remove service mapping with the specified name.
        /// </summary>
        /// <param name="name">The service name.</param>
        public void Remove(string name)
        {
            _mappings.Remove(name);
        }

        /// <summary>
        /// Add a service mapping to the collection.
        /// </summary>
        /// <param name="service">The service mapping to add.</param>
        public void Add(ServiceMapping service)
        {
            _mappings.Add(service);
        }

        /// <summary>
        /// Add a service mapping to the collection with the specified name and predicate.
        /// </summary>
        /// <param name="name">The service name.</param>
        /// <param name="predicate">The predicate used to filter <see cref="HealthResult"/> instances. These results determine service health.</param>
        public void MapService(string name, Func<HealthResult, bool> predicate)
        {
            _mappings.Remove(name);
            _mappings.Add(new ServiceMapping(name, predicate));
        }

        /// <inheritdoc />
        public IEnumerator<ServiceMapping> GetEnumerator()
        {
            return _mappings.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _mappings.GetEnumerator();
        }
    }
}
