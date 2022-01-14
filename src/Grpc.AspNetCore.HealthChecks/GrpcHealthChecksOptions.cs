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

namespace Grpc.AspNetCore.HealthChecks
{
    /// <summary>
    /// Contains options for the gRPC health checks service.
    /// </summary>
    public class GrpcHealthChecksOptions
    {
        /// <summary>
        /// Gets or sets a predicate that is used to filter the set of health report entries.
        /// Filtered out entries aren't reported by the gRPC health checks service.
        /// </summary>
        /// <remarks>
        /// If <see cref="Filter"/> is <c>null</c>, the gRPC health checks service will use all
        /// health report entries - this is the default behavior. To use a subset of health report entries,
        /// provide a function that filters the set of entries.
        /// </remarks>
        public Func<string, HealthReportEntry, bool>? Filter { get; set; }
    }
}
