using Grpc.AspNetCore.HealthChecks;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal static class GrpcHealthChecksPublisherHelpers
{
    public static List<ServiceStatus> CalculateStatuses(HealthReport report, GrpcHealthChecksOptions options)
    {
        var statuses = new List<ServiceStatus>();
        foreach (var registration in options.Services)
        {
            var resolvedStatus = GetStatus(report, registration.Predicate);

            statuses.Add(new ServiceStatus(registration.Name, resolvedStatus));
        }

        return statuses;
    }

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

internal record struct ServiceStatus(string Name, HealthCheckResponse.Types.ServingStatus Status);
