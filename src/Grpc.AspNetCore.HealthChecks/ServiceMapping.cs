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
    public ServiceMapping(string name, Func<HealthCheckMapContext, bool> predicate)
    {
        Name = name;
        HealthCheckPredicate = predicate;
    }

    /// <summary>
    /// Gets the service name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the predicate used to filter health checks when the <c>Health</c> service <c>Check</c> and <c>Watch</c> methods are called.
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
    /// </summary>
    public Func<HealthCheckMapContext, bool>? HealthCheckPredicate { get; }

    /// <summary>
    /// Gets the predicate used to filter <see cref="HealthResult"/> instances. These results determine service health.
    /// </summary>
    [Obsolete($"This member is obsolete and will be removed in the future. Use {nameof(HealthCheckPredicate)} to map service names to .NET health checks.")]
    public Func<HealthResult, bool>? Predicate { get; }
}
