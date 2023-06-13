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

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Grpc.AspNetCore.HealthChecks;

/// <summary>
/// Represents the result of a single <see cref="IHealthCheck"/>.
/// </summary>
[Obsolete($"HealthResult is obsolete and will be removed in a future release. Use {nameof(HealthCheckMapContext)} instead.")]
public sealed class HealthResult
{
    /// <summary>
    /// Creates a new instance of <see cref="HealthResult"/>.
    /// </summary>
    /// <param name="name">The health check name.</param>
    /// <param name="tags">Tags associated with the health check.</param>
    /// <param name="status">A value indicating the health status of the component that was checked.</param>
    /// <param name="description">A human-readable description of the status of the component that was checked.</param>
    /// <param name="duration">A value indicating the health execution duration.</param>
    /// <param name="exception">An <see cref="Exception"/> representing the exception that was thrown when checking for status (if any).</param>
    /// <param name="data">Additional key-value pairs describing the health of the component.</param>
    public HealthResult(string name, IEnumerable<string> tags, HealthStatus status, string? description, TimeSpan duration, Exception? exception, IReadOnlyDictionary<string, object> data)
    {
        Name = name;
        Tags = tags;
        Status = status;
        Description = description;
        Data = data;
        Exception = exception;
        Duration = duration;
    }

    /// <summary>
    /// Gets the health check name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IEnumerable<string> Tags { get; }

    /// <summary>
    /// Gets the health status of the component that was checked.
    /// </summary>
    public HealthStatus Status { get; }

    /// <summary>
    /// Gets a human-readable description of the status of the component that was checked.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets additional key-value pairs describing the health of the component.
    /// </summary>
    public IReadOnlyDictionary<string, object> Data { get; }

    /// <summary>
    /// Gets an <see cref="System.Exception"/> representing the exception that was thrown when checking for status (if any).
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Gets a human-readable description of the status of the component that was checked.
    /// </summary>
    public TimeSpan Duration { get; }
}
