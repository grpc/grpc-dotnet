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
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HealthChecks;

[TestFixture(true)]
[TestFixture(false)]
public class GrpcHealthChecksPublisherTests
{
    private readonly bool _testOldMapService;

    public GrpcHealthChecksPublisherTests(bool testOldMapService)
    {
        _testOldMapService = testOldMapService;
    }

    [Test]
    public async Task PublishAsync_Check_ChangingStatus()
    {
        // Arrange
        var healthService = new HealthServiceImpl();
        var publisher = CreatePublisher(healthService);

        HealthCheckResponse response;

        // Act 1
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => healthService.Check(new HealthCheckRequest { Service = "" }, context: null!));

        // Assert 1
        Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);

        // Act 2
        var report = CreateSimpleHealthReport(HealthStatus.Healthy);
        await publisher.PublishAsync(report, CancellationToken.None);

        response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null!);

        // Assert 2
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

        // Act 3
        report = CreateSimpleHealthReport(HealthStatus.Unhealthy);
        await publisher.PublishAsync(report, CancellationToken.None);

        response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null!);

        // Act 3
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response.Status);
    }

    [Test]
    public async Task PublishAsync_CheckWithFilter_ChangingStatusBasedOnFilter()
    {
        // Arrange
        var healthService = new HealthServiceImpl();
        var publisher = CreatePublisher(
            healthService,
            o =>
            {
                Map(o.Services, "", (name, tags) => !tags.Contains("exclude"));
            });

        HealthCheckResponse response;

        // Act 1
        var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => healthService.Check(new HealthCheckRequest { Service = "" }, context: null!));

        // Assert 1
        Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);

        // Act 2
        var report = CreateSimpleHealthReport(
            new HealthResult("", HealthStatus.Healthy),
            new HealthResult("other", HealthStatus.Healthy, new[] { "exclude" }));
        await publisher.PublishAsync(report, CancellationToken.None);

        response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null!);

        // Assert 2
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

        // Act 3
        report = CreateSimpleHealthReport(
            new HealthResult("", HealthStatus.Healthy),
            new HealthResult("other", HealthStatus.Unhealthy, new[] { "exclude" }));
        await publisher.PublishAsync(report, CancellationToken.None);

        response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null!);

        // Act 3
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

        // Act 4
        report = CreateSimpleHealthReport(
            new HealthResult("", HealthStatus.Unhealthy),
            new HealthResult("other", HealthStatus.Unhealthy, new[] { "exclude" }));
        await publisher.PublishAsync(report, CancellationToken.None);

        response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null!);

        // Act 4
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response.Status);
    }

    private record struct HealthResult(string Name, HealthStatus Status, IEnumerable<string>? Tags = null);

    private static HealthReport CreateSimpleHealthReport(HealthStatus healthStatus, IEnumerable<string>? tags = null)
    {
        return CreateSimpleHealthReport(new HealthResult("", healthStatus, tags));
    }

    private static HealthReport CreateSimpleHealthReport(params HealthResult[] results)
    {
        var entries = new Dictionary<string, HealthReportEntry>();

        foreach (var result in results)
        {
            entries[result.Name] = new HealthReportEntry(result.Status, "Description!", TimeSpan.Zero, exception: null, data: null, tags: result.Tags);
        }

        return new HealthReport(entries, TimeSpan.Zero);
    }

    [Test]
    public async Task PublishAsync_Check_MapStatuses()
    {
        // Arrange
        var healthService = new HealthServiceImpl();
        var publisher = CreatePublisher(healthService, o =>
        {
            Map(o.Services, nameof(HealthStatus.Healthy), (name, tags) => name == nameof(HealthStatus.Healthy));
            Map(o.Services, nameof(HealthStatus.Degraded), (name, tags) => name == nameof(HealthStatus.Degraded));
            Map(o.Services, nameof(HealthStatus.Unhealthy), (name, tags) => name == nameof(HealthStatus.Unhealthy));
        });

        // Act
        HealthCheckResponse response;

        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                [nameof(HealthStatus.Healthy)] = new HealthReportEntry(HealthStatus.Healthy, "Description!", TimeSpan.Zero, exception: null, data: null),
                [nameof(HealthStatus.Degraded)] = new HealthReportEntry(HealthStatus.Degraded, "Description!", TimeSpan.Zero, exception: null, data: null),
                [nameof(HealthStatus.Unhealthy)] = new HealthReportEntry(HealthStatus.Unhealthy, "Description!", TimeSpan.Zero, exception: null, data: null)
            },
            TimeSpan.Zero);
        await publisher.PublishAsync(report, CancellationToken.None);

        // Assert
        response = await healthService.Check(new HealthCheckRequest { Service = nameof(HealthStatus.Healthy) }, context: null!);
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

        response = await healthService.Check(new HealthCheckRequest { Service = nameof(HealthStatus.Degraded) }, context: null!);
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

        response = await healthService.Check(new HealthCheckRequest { Service = nameof(HealthStatus.Unhealthy) }, context: null!);
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response.Status);
    }

    [Test]
    public async Task PublishAsync_Watch_ChangingStatus()
    {
        // Arrange
        var healthService = new HealthServiceImpl();
        var publisher = CreatePublisher(healthService);
        var responseStream = new TestServerStreamWriter<HealthCheckResponse>();
        var cts = new CancellationTokenSource();
        var serverCallContext = new TestServerCallContext(DateTime.MinValue, cts.Token);

        // Act 1
        var call = healthService.Watch(new HealthCheckRequest { Service = "" }, responseStream, serverCallContext);

        // Assert 1
        await TestHelpers.AssertIsTrueRetryAsync(() => responseStream.Responses.Count == 1, "Unexpected response count.");
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.ServiceUnknown, responseStream.Responses.Last().Status);

        // Act 2
        await publisher.PublishAsync(CreateSimpleHealthReport(HealthStatus.Healthy), CancellationToken.None);

        // Assert 2
        await TestHelpers.AssertIsTrueRetryAsync(() => responseStream.Responses.Count == 2, "Unexpected response count.");
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, responseStream.Responses.Last().Status);

        // Act 3
        await publisher.PublishAsync(CreateSimpleHealthReport(HealthStatus.Degraded), CancellationToken.None);

        // Act 3
        await TestHelpers.AssertIsTrueRetryAsync(() => responseStream.Responses.Count == 2, "Unexpected response count.");
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, responseStream.Responses.Last().Status);

        // Act 4
        await publisher.PublishAsync(CreateSimpleHealthReport(HealthStatus.Unhealthy), CancellationToken.None);

        // Act 4
        await TestHelpers.AssertIsTrueRetryAsync(() => responseStream.Responses.Count == 3, "Unexpected response count.");
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, responseStream.Responses.Last().Status);

        // End call
        cts.Cancel();
        await call.DefaultTimeout();

        // Act 4
        await publisher.PublishAsync(CreateSimpleHealthReport(HealthStatus.Healthy), CancellationToken.None);

        // Act 4
        await TestHelpers.AssertIsTrueRetryAsync(() => responseStream.Responses.Count == 3, "Unexpected response count.");
        Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, responseStream.Responses.Last().Status);
    }

    private GrpcHealthChecksPublisher CreatePublisher(HealthServiceImpl healthService, Action<GrpcHealthChecksOptions>? configureOptions = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));
        services.AddNUnitLogger();
        var serviceProvider = services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var options = new GrpcHealthChecksOptions();
        Map(options.Services, "", (_, __) => true);
        configureOptions?.Invoke(options);
        return new GrpcHealthChecksPublisher(healthService, Options.Create(options), loggerFactory);
    }

    private void Map(ServiceMappingCollection mappings, string name, Func<string, IEnumerable<string>, bool> predicate)
    {
        if (_testOldMapService)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            mappings.MapService(name, r => predicate(r.Name, r.Tags));
#pragma warning restore CS0618 // Type or member is obsolete
        }
        else
        {
            mappings.Map(name, r => predicate(r.Name, r.Tags));
        }
    }
}
