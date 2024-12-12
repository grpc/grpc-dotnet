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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.HealthChecks;

internal sealed partial class GrpcHealthChecksPublisher : IHealthCheckPublisher
{
    private readonly HealthServiceImpl _healthService;
    private readonly ILogger _logger;
    private readonly GrpcHealthChecksOptions _options;

    public GrpcHealthChecksPublisher(HealthServiceImpl healthService, IOptions<GrpcHealthChecksOptions> options, ILoggerFactory loggerFactory)
    {
        _healthService = healthService;
        _options = options.Value;
        _logger = loggerFactory.CreateLogger<GrpcHealthChecksPublisher>();
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        Log.EvaluatingPublishedHealthReport(_logger, report.Entries.Count, _options.Services.Count);

        foreach (var serviceMapping in _options.Services)
        {
            IEnumerable<KeyValuePair<string, HealthReportEntry>> serviceEntries = report.Entries;

            if (serviceMapping.HealthCheckPredicate != null)
            {
                serviceEntries = serviceEntries.Where(entry =>
                {
                    var context = new HealthCheckMapContext(entry.Key, entry.Value.Tags);
                    return serviceMapping.HealthCheckPredicate(context);
                });
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (serviceMapping.Predicate != null)
            {
                serviceEntries = serviceEntries.Where(entry =>
                {
                    var result = new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data);
                    return serviceMapping.Predicate(result);
                });
            }
#pragma warning restore CS0618 // Type or member is obsolete

            var (resolvedStatus, resultCount) = HealthChecksStatusHelpers.GetStatus(serviceEntries);

            Log.ServiceMappingStatusUpdated(_logger, serviceMapping.Name, resolvedStatus, resultCount);
            _healthService.SetStatus(serviceMapping.Name, resolvedStatus);
        }

        return Task.CompletedTask;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Trace, EventId = 1, EventName = "EvaluatingPublishedHealthReport", Message = "Evaluating {HealthReportEntryCount} published health report entries against {ServiceMappingCount} service mappings.")]
        public static partial void EvaluatingPublishedHealthReport(ILogger logger, int healthReportEntryCount, int serviceMappingCount);

        [LoggerMessage(Level = LogLevel.Debug, EventId = 2, EventName = "ServiceMappingStatusUpdated", Message = "Service '{ServiceName}' status updated to {Status}. {EntriesCount} health report entries evaluated.")]
        public static partial void ServiceMappingStatusUpdated(ILogger logger, string serviceName, HealthCheckResponse.Types.ServingStatus status, int entriesCount);

    }
}
