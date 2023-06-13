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
using System.Diagnostics.CodeAnalysis;

namespace Grpc.AspNetCore.HealthChecks;

/// <summary>
/// A collection of <see cref="ServiceMapping"/> used to map health results to gRPC health checks services.
/// </summary>
public sealed class ServiceMappingCollection : ICollection<ServiceMapping>
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
    /// Gets the number of service mappings.
    /// </summary>
    public int Count => _mappings.Count;

    bool ICollection<ServiceMapping>.IsReadOnly => false;

    internal bool TryGetServiceMapping(string name, [NotNullWhen(true)] out ServiceMapping? serviceMapping)
    {
        return _mappings.TryGetValue(name, out serviceMapping);
    }

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
    [Obsolete("This method is obsolete and will be removed in the future. Use Map(string name, Func<HealthCheckRegistration, bool> predicate) to map service names to .NET health checks.")]
    public void MapService(string name, Func<HealthResult, bool> predicate)
    {
        _mappings.Remove(name);
        _mappings.Add(new ServiceMapping(name, predicate));
    }

    /// <summary>
    /// Add a service mapping to the collection with the specified name and predicate.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <param name="predicate">
    /// The predicate used to filter health checks when the <c>Health</c> service <c>Check</c> and <c>Watch</c> methods are called.
    /// <para>
    /// The <c>Health</c> service methods have different behavior:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>Check</c> uses the predicate to determine which health checks are run for a service.</description></item>
    /// <item><description><c>Watch</c> periodically runs all health checks. The predicate filters the health results for a service.</description></item>
    /// </list>
    /// <para>
    /// The health result for the service is based on the health check results.
    /// </para>
    /// </param>
    public void Map(string name, Func<HealthCheckMapContext, bool> predicate)
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

    bool ICollection<ServiceMapping>.Contains(ServiceMapping item)
    {
        return _mappings.Contains(item);
    }

    void ICollection<ServiceMapping>.CopyTo(ServiceMapping[] array, int arrayIndex)
    {
        _mappings.CopyTo(array, arrayIndex);
    }

    bool ICollection<ServiceMapping>.Remove(ServiceMapping item)
    {
        return _mappings.Remove(item);
    }
}
