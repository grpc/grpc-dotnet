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

using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal static class HealthChecksStatusHelpers
{
    public static (HealthCheckResponse.Types.ServingStatus status, int resultCount) GetStatus(IEnumerable<KeyValuePair<string, HealthReportEntry>> results)
    {
        var resultCount = 0;
        var resolvedStatus = HealthCheckResponse.Types.ServingStatus.Unknown;
        foreach (var result in results)
        {
            resultCount++;

            // NotServing is a final status but keep iterating to discover how many results are being evaluated.
            if (resolvedStatus == HealthCheckResponse.Types.ServingStatus.NotServing)
            {
                continue;
            }

            if (result.Value.Status == HealthStatus.Unhealthy)
            {
                resolvedStatus = HealthCheckResponse.Types.ServingStatus.NotServing;
            }
            else
            {
                resolvedStatus = HealthCheckResponse.Types.ServingStatus.Serving;
            }
        }

        return (resolvedStatus, resultCount);
    }
}
