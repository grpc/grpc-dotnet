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
/// Context used to map health check registrations to a service.
/// </summary>
public sealed class HealthCheckMapContext
{
    /// <summary>
    /// Creates a new instance of <see cref="HealthCheckMapContext"/>.
    /// </summary>
    /// <param name="name">The health check name.</param>
    /// <param name="tags">Tags associated with the health check.</param>
    public HealthCheckMapContext(string name, IEnumerable<string> tags)
    {
        Name = name;
        Tags = tags;
    }

    /// <summary>
    /// Gets the health check name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the tags associated with the health check.
    /// </summary>
    public IEnumerable<string> Tags { get; }
}
