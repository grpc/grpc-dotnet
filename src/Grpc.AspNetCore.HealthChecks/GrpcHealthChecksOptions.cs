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
/// Contains options for the gRPC health checks service.
/// </summary>
public sealed class GrpcHealthChecksOptions
{
    /// <summary>
    /// Gets a collection of service mappings used to map health results to gRPC health checks services.
    /// </summary>
    public ServiceMappingCollection Services { get; } = new ServiceMappingCollection();

    /// <summary>
    /// Gets or sets a value indicating whether the <c>Check</c> method runs health checks when it is called.
    /// If <see cref="RunHealthChecksOnCheck" /> is <c>false</c> then cached health check results previously published by 
    /// <see cref="IHealthCheckPublisher"/> are returned by the <c>Check</c> method.
    /// </summary>
    public bool RunHealthChecksOnCheck { get; set; } = true;
}
