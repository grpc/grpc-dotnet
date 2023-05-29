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

using Grpc.HealthCheck;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.HealthChecks;

internal sealed class GrpcHealthChecksPublisher : IHealthCheckPublisher
{
    private readonly HealthServiceImpl _healthService;
    private readonly ILogger _logger;
    private readonly GrpcHealthChecksOptions _options;

    public GrpcHealthChecksPublisher(HealthServiceImpl healthService, IOptions<GrpcHealthChecksOptions> options, ILoggerFactory loggerFactory)
    {
        _healthService = healthService;
        _logger = loggerFactory.CreateLogger<GrpcHealthChecksPublisher>();
        _options = options.Value;
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var serviceMapping in _options.Services)
        {
            var entries = report.Entries.ToList();

            if (serviceMapping.FilterPredicate != null)
            {
                for (var i = entries.Count - 1; i >= 0; i--)
                {
                    var entry = entries[i];
                    var registration = new HealthCheckFilterContext(entry.Key, entry.Value.Tags);

                    if (!serviceMapping.FilterPredicate(registration))
                    {
                        entries.RemoveAt(i);
                    }
                }
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var results = entries.Select(entry => new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data));
            if (serviceMapping.Predicate != null)
            {
                results = results.Where(serviceMapping.Predicate);
            }
            var resolvedStatus = HealthChecksStatusHelpers.GetStatus(results);
#pragma warning restore CS0618 // Type or member is obsolete

            _healthService.SetStatus(serviceMapping.Name, resolvedStatus);
        }

        return Task.CompletedTask;
    }
}
