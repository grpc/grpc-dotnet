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

internal sealed class GrpcHealthChecksPublisher : IHealthCheckPublisher
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

        List<KeyValuePair<string, HealthReportEntry>>? serviceEntries = null;
        foreach (var serviceMapping in _options.Services)
        {
            serviceEntries ??= new();
            serviceEntries.AddRange(report.Entries);

            if (serviceMapping.HealthCheckPredicate != null)
            {
                for (var i = serviceEntries.Count - 1; i >= 0; i--)
                {
                    var entry = serviceEntries[i];
                    var registration = new HealthCheckFilterContext(entry.Key, entry.Value.Tags);

                    if (!serviceMapping.HealthCheckPredicate(registration))
                    {
                        serviceEntries.RemoveAt(i);
                    }
                }
            }

#pragma warning disable CS0618 // Type or member is obsolete
            var results = serviceEntries.Select(entry => new HealthResult(entry.Key, entry.Value.Tags, entry.Value.Status, entry.Value.Description, entry.Value.Duration, entry.Value.Exception, entry.Value.Data));
            if (serviceMapping.Predicate != null)
            {
                results = results.Where(serviceMapping.Predicate);
            }
            var (resolvedStatus, resultCount) = HealthChecksStatusHelpers.GetStatus(results);

#pragma warning restore CS0618 // Type or member is obsolete

            Log.ServiceMappingStatusUpdated(_logger, serviceMapping.Name, resolvedStatus, resultCount);
            _healthService.SetStatus(serviceMapping.Name, resolvedStatus);

            serviceEntries.Clear();
        }

        return Task.CompletedTask;
    }

    private static class Log
    {
        private static readonly Action<ILogger, int, int, Exception?> _evaluatingPublishedHealthReport =
            LoggerMessage.Define<int, int>(LogLevel.Trace, new EventId(1, "EvaluatingPublishedHealthReport"), "Evaluating {HealthReportEntryCount} published health report entries against {ServiceMappingCount} service mappings.");

        private static readonly Action<ILogger, string, HealthCheckResponse.Types.ServingStatus, int, Exception?> _serviceMappingStatusUpdated =
            LoggerMessage.Define<string, HealthCheckResponse.Types.ServingStatus, int>(LogLevel.Debug, new EventId(2, "ServiceMappingStatusUpdated"), "Service '{ServiceName}' status updated to {Status}. {EntriesCount} health report entries evaluated.");

        public static void EvaluatingPublishedHealthReport(ILogger logger, int healthReportEntryCount, int serviceMappingCount)
        {
            _evaluatingPublishedHealthReport(logger, healthReportEntryCount, serviceMappingCount, null);
        }

        public static void ServiceMappingStatusUpdated(ILogger logger, string serviceName, HealthCheckResponse.Types.ServingStatus status, int entriesCount)
        {
            _serviceMappingStatusUpdated(logger, serviceName, status, entriesCount, null);
        }
    }
}
