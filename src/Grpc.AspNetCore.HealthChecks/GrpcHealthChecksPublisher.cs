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
            foreach (var entry in report.Entries)
            {
                if (_options.Filter != null && !_options.Filter(new HealthResultKey(entry.Key, entry.Value.Tags)))
                {
                    continue;
                }

                var status = entry.Value.Status;
                _healthService.SetStatus(entry.Key, ResolveStatus(status));
            }

            return Task.CompletedTask;
        }

        private static HealthCheckResponse.Types.ServingStatus ResolveStatus(HealthStatus status)
        {
            return status == HealthStatus.Unhealthy
                ? HealthCheckResponse.Types.ServingStatus.NotServing
                : HealthCheckResponse.Types.ServingStatus.Serving;
        }
    }

}
