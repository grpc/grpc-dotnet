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

namespace Grpc.AspNetCore.HealthChecks;

/// <summary>
/// Represents the mapping of health check results to a service exposed by the gRPC health checks API.
/// </summary>
public sealed class ServiceMapping
{
    /// <summary>
    /// Creates a new instance of <see cref="ServiceMapping"/>.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <param name="predicate">The predicate used to filter <see cref="HealthResult"/> instances. These results determine service health.</param>
    [Obsolete("This constructor is obsolete and will be removed in the future. Use ServiceMapping(string name, Func<HealthCheckRegistration, bool> predicate) to map service names to .NET health checks.")]
    public ServiceMapping(string name, Func<HealthResult, bool> predicate)
    {
        Name = name;
        Predicate = predicate;
    }

    /// <summary>
    /// Creates a new instance of <see cref="ServiceMapping"/>.
    /// </summary>
    /// <param name="name">The service name.</param>
    /// <param name="predicate">The predicate used to filter <see cref="HealthResult"/> instances. These results determine service health.</param>
    public ServiceMapping(string name, Func<HealthCheckFilterContext, bool> predicate)
    {
        Name = name;
        FilterPredicate = predicate;
    }

    /// <summary>
    /// Gets the service name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the predicate used to filter registered health check instances. These results determine service health.
    /// <para>
    /// 
    /// </para>
    /// </summary>
    public Func<HealthCheckFilterContext, bool>? FilterPredicate { get; }

    /// <summary>
    /// Gets the predicate used to filter <see cref="HealthResult"/> instances. These results determine service health.
    /// </summary>
    [Obsolete($"This member is obsolete and will be removed in the future. Use {nameof(FilterPredicate)} to map service names to .NET health checks.")]
    public Func<HealthResult, bool>? Predicate { get; }
}
