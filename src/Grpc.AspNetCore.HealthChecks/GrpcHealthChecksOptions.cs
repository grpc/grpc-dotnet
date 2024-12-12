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
using Microsoft.Extensions.Hosting;

namespace Grpc.AspNetCore.HealthChecks;

/// <summary>
/// Contains options for the gRPC health checks service.
/// </summary>
public sealed class GrpcHealthChecksOptions
{
    /// <summary>
    /// Gets a collection of service mappings used to map health results to gRPC health checks services.
    /// </summary>
    public ServiceMappingCollection Services { get; } = new ServiceMappingCollection();

    /// <summary>
    /// Gets or sets a value indicating whether methods use cached health checks results. 
    /// The default value is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// When <c>false</c>, health checks are recalculated and returned. When <c>true</c>, cached health check results previously
    /// published by <see cref="IHealthCheckPublisher"/> are returned.
    /// </remarks>
    public bool UseHealthChecksCache { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to suppress completing <c>Watch</c> health check calls when the application begins shutting down.
    /// The default value is <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When <c>false</c>, health checks <c>Watch</c> calls are completed with a status of NotServing when the server application begins shutting down.
    /// Shutdown is indicated by the <see cref="IHostApplicationLifetime.ApplicationStopping"/> token being raised and causes <c>Watch</c> to complete.
    /// When <c>true</c>, health checks <c>Watch</c> calls are left running. Running calls will be eventually be forcefully aborted when the server finishes shutting down.
    /// </para>
    /// <para>
    /// Completing the <c>Watch</c> call allows the server to gracefully exit. If <c>Watch</c> calls aren't shutdown then the server runs until
    /// <see cref="HostOptions.ShutdownTimeout"/> is exceeded and the server forcefully aborts remaining active requests.
    /// </para>
    /// </remarks>
    public bool SuppressCompletionOnShutdown { get; set; }
}
