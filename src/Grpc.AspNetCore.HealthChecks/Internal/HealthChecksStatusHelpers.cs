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

using Grpc.AspNetCore.HealthChecks;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal static class HealthChecksStatusHelpers
{
    public static HealthCheckResponse.Types.ServingStatus GetStatus(HealthReport report, Func<HealthResult, bool> predicate)
    {
        var filteredResults = report.Entries
            .Select(entry => new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data))
            .Where(predicate);

        var resolvedStatus = HealthCheckResponse.Types.ServingStatus.Unknown;
        foreach (var result in filteredResults)
        {
            if (result.Status == HealthStatus.Unhealthy)
            {
                resolvedStatus = HealthCheckResponse.Types.ServingStatus.NotServing;

                // No point continuing to check statuses.
                break;
            }

            resolvedStatus = HealthCheckResponse.Types.ServingStatus.Serving;
        }

        return resolvedStatus;
    }
}
