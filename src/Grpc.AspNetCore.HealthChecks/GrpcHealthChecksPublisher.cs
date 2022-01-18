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
using Grpc.HealthCheck;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.HealthChecks
{
    internal sealed class GrpcHealthChecksPublisher : IHealthCheckPublisher
    {
        private readonly HealthServiceImpl _healthService;
        private readonly GrpcHealthChecksOptions _options;

        public GrpcHealthChecksPublisher(HealthServiceImpl healthService, IOptions<GrpcHealthChecksOptions> options)
        {
            _healthService = healthService;
            _options = options.Value;
        }

        public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            foreach (var registration in _options.Services)
            {
                var filteredResults = report.Entries
                    .Select(entry => new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data))
                    .Where(registration.Predicate);

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

                _healthService.SetStatus(registration.Name, resolvedStatus);
            }

            return Task.CompletedTask;
        }
    }
}
