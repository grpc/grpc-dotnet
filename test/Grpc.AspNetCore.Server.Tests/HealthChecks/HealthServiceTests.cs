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

using System.Diagnostics;
using Grpc.AspNetCore.HealthChecks;
using Grpc.AspNetCore.HealthChecks.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HealthChecks;

[TestFixture(true)]
[TestFixture(false)]
public class HealthServiceTests
{
    private readonly bool _testOldMapService;

    public HealthServiceTests(bool testOldMapService)
    {
        _testOldMapService = testOldMapService;
    }

    [Test]
    public async Task HealthService_Watch_UsePublishedChecks_WriteResults()
    {
        // Arrange
        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGrpcHealthChecks(o => o.UseHealthChecksCache = true).AddAsyncCheck(
            "",
            () => Task.FromResult(healthCheckResult), new string[] { "sample" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        HealthCheckResponse? response = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testServerStreamWriter = new TestServerStreamWriter<HealthCheckResponse>();
        testServerStreamWriter.OnWriteAsync = async message =>
        {
            response = message;
            await syncPoint.WaitToContinue();
        };

        var cts = new CancellationTokenSource();
        var callTask = healthService.Watch(
            new HealthCheckRequest(),
            testServerStreamWriter,
            new TestServerCallContext(DateTime.MaxValue, cts.Token));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.ServiceUnknown, response!.Status);
            syncPoint.Continue();

            healthCheckResult = new HealthCheckResult(HealthStatus.Unhealthy);
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response!.Status);
            syncPoint.Continue();

            healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response!.Status);
            syncPoint.Continue();

            cts.Cancel();

            await callTask.DefaultTimeout();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task HealthService_Watch_RunChecks_WriteResults()
    {
        // Arrange
        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNUnitLogger();
        services.AddGrpcHealthChecks().AddAsyncCheck(
            "",
            () => Task.FromResult(healthCheckResult), new string[] { "sample" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        HealthCheckResponse? response = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testServerStreamWriter = new TestServerStreamWriter<HealthCheckResponse>();
        testServerStreamWriter.OnWriteAsync = async message =>
        {
            response = message;
            await syncPoint.WaitToContinue();
        };

        var cts = new CancellationTokenSource();
        var callTask = healthService.Watch(
            new HealthCheckRequest(),
            testServerStreamWriter,
            new TestServerCallContext(DateTime.MaxValue, cts.Token));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response!.Status);
            syncPoint.Continue();

            healthCheckResult = new HealthCheckResult(HealthStatus.Unhealthy);
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response!.Status);
            syncPoint.Continue();

            healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response!.Status);
            syncPoint.Continue();

            cts.Cancel();

            await callTask.DefaultTimeout();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task HealthService_Watch_RunChecks_Log()
    {
        // Arrange
        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);

        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging(o => o.AddProvider(testProvider).SetMinimumLevel(LogLevel.Debug));
        services.AddNUnitLogger();
        services.AddGrpcHealthChecks().AddAsyncCheck(
            "",
            () => Task.FromResult(healthCheckResult), new string[] { "sample" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        HealthCheckResponse? response = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testServerStreamWriter = new TestServerStreamWriter<HealthCheckResponse>();
        testServerStreamWriter.OnWriteAsync = async message =>
        {
            response = message;
            await syncPoint.WaitToContinue();
        };

        var cts = new CancellationTokenSource();
        var callTask = healthService.Watch(
            new HealthCheckRequest(),
            testServerStreamWriter,
            new TestServerCallContext(DateTime.MaxValue, cts.Token));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response!.Status);
            syncPoint.Continue();

            healthCheckResult = new HealthCheckResult(HealthStatus.Unhealthy);
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response!.Status);
            syncPoint.Continue();

            cts.Cancel();

            await callTask.DefaultTimeout();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }

        var writes = testSink.Writes.ToList();
        var evaluatingPublishedHealthReport = writes.Single(w => w.EventId.Name == "EvaluatingPublishedHealthReport");
        var serviceMappingStatusUpdated = writes.Single(w => w.EventId.Name == "ServiceMappingStatusUpdated");

        Assert.AreEqual("Evaluating 1 published health report entries against 1 service mappings.", evaluatingPublishedHealthReport.Message);
        Assert.AreEqual("Service '' status updated to NotServing. 1 health report entries evaluated.", serviceMappingStatusUpdated.Message);
    }

    [Test]
    public async Task HealthService_Watch_ServerShutdownDuringCall_WatchCompleted()
    {
        // Arrange
        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);

        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging(o => o.AddProvider(testProvider).SetMinimumLevel(LogLevel.Debug));
        services.AddNUnitLogger();
        services.AddGrpcHealthChecks().AddAsyncCheck(
            "",
            () => Task.FromResult(healthCheckResult), new string[] { "sample" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        HealthCheckResponse? response = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testServerStreamWriter = new TestServerStreamWriter<HealthCheckResponse>();
        testServerStreamWriter.OnWriteAsync = async message =>
        {
            response = message;
            await syncPoint.WaitToContinue();
        };

        var cts = new CancellationTokenSource();
        var callTask = healthService.Watch(
            new HealthCheckRequest(),
            testServerStreamWriter,
            new TestServerCallContext(DateTime.MaxValue, cts.Token));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response!.Status);
            syncPoint.Continue();

            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            lifetime.StopApplication();
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response!.Status);
            syncPoint.Continue();

            await callTask.DefaultTimeout();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task HealthService_Watch_ServerShutdownBeforeCall_WatchCompleted()
    {
        // Arrange
        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);

        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging(o => o.AddProvider(testProvider).SetMinimumLevel(LogLevel.Debug));
        services.AddNUnitLogger();
        services.AddGrpcHealthChecks().AddAsyncCheck(
            "",
            () => Task.FromResult(healthCheckResult), new string[] { "sample" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        HealthCheckResponse? response = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testServerStreamWriter = new TestServerStreamWriter<HealthCheckResponse>();
        testServerStreamWriter.OnWriteAsync = async message =>
        {
            response = message;
            await syncPoint.WaitToContinue();
        };

        lifetime.StopApplication();

        var cts = new CancellationTokenSource();
        var callTask = healthService.Watch(
            new HealthCheckRequest(),
            testServerStreamWriter,
            new TestServerCallContext(DateTime.MaxValue, cts.Token));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response!.Status);
            syncPoint.Continue();

            await callTask.DefaultTimeout();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task HealthService_Watch_ServerShutdown_SuppressCompletion_WatchNotCompleted()
    {
        // Arrange
        var testSink = new TestSink();
        var testProvider = new TestLoggerProvider(testSink);

        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging(o => o.AddProvider(testProvider).SetMinimumLevel(LogLevel.Debug));
        services.AddNUnitLogger();
        services.AddGrpcHealthChecks(o => o.SuppressCompletionOnShutdown = true).AddAsyncCheck(
            "",
            () => Task.FromResult(healthCheckResult), new string[] { "sample" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        HealthCheckResponse? response = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testServerStreamWriter = new TestServerStreamWriter<HealthCheckResponse>();
        testServerStreamWriter.OnWriteAsync = async message =>
        {
            response = message;
            await syncPoint.WaitToContinue();
        };

        var cts = new CancellationTokenSource();
        var callTask = healthService.Watch(
            new HealthCheckRequest(),
            testServerStreamWriter,
            new TestServerCallContext(DateTime.MaxValue, cts.Token));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response!.Status);
            syncPoint.Continue();

            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            lifetime.StopApplication();

            // Wait a second to double check that watch doesn't complete.
            var waitForSyncPointTask = syncPoint.WaitForSyncPoint();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(0.5));
            if (await Task.WhenAny(callTask, waitForSyncPointTask, timeoutTask) != timeoutTask)
            {
                Assert.Fail("Expected watch to not complete.");
            }

            cts.Cancel();

            await callTask.DefaultTimeout();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    private class TestHealthCheckPublisher : IHealthCheckPublisher
    {
        public Func<HealthReport, Task>? OnHealthReport { get; set; }

        public async Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
        {
            if (OnHealthReport != null)
            {
                await OnHealthReport(report);
            }
        }
    }

    private static HealthServiceIntegration CreateService(IServiceProvider serviceProvider)
    {
        return new HealthServiceIntegration(
            serviceProvider.GetRequiredService<HealthServiceImpl>(),
            serviceProvider.GetRequiredService<IOptions<HealthCheckOptions>>(),
            serviceProvider.GetRequiredService<IOptions<GrpcHealthChecksOptions>>(),
            serviceProvider.GetRequiredService<HealthCheckService>(),
            serviceProvider.GetRequiredService<IHostApplicationLifetime>());
    }

    [Test]
    public async Task HealthService_CheckWithFilter_RunChecks_FilteredResultsExcluded()
    {
        // Arrange
        var healthCheckResults = new List<HealthCheckResult>();
        var healthCheckStatus = HealthStatus.Healthy;

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace));
        services.AddNUnitLogger();
        services
            .AddGrpcHealthChecks(o =>
            {
                Map(o.Services, "", (name, tags) => !tags.Contains("exclude"));
            })
            .AddAsyncCheck("default", () => RunHealthCheck(healthCheckStatus, ""))
            .AddAsyncCheck("filtered", () => RunHealthCheck(healthCheckStatus, "filtered"), new string[] { "exclude" });

        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);

        async Task CheckForStatusAsync(string service, HealthCheckResponse.Types.ServingStatus status)
        {
            var context = new TestServerCallContext(DateTime.MaxValue, CancellationToken.None);

            var result = await healthService!.Check(new HealthCheckRequest() { Service = service }, context);

            Assert.AreEqual(status, result.Status);
        }

        // Act & Assert
        await CheckForStatusAsync(service: "", HealthCheckResponse.Types.ServingStatus.Serving);

        AssertHealthCheckResults(HealthStatus.Healthy);

        healthCheckStatus = HealthStatus.Unhealthy;
        await CheckForStatusAsync(service: "", HealthCheckResponse.Types.ServingStatus.NotServing);

        AssertHealthCheckResults(HealthStatus.Unhealthy);

        await ExceptionAssert.ThrowsAsync<RpcException>(() => CheckForStatusAsync(service: "filtered", HealthCheckResponse.Types.ServingStatus.ServiceUnknown));

        Assert.AreEqual(0, healthCheckResults.Count);

        void AssertHealthCheckResults(HealthStatus healthStatus)
        {
            // Health checks are run in parallel. Sort to ensure a consistent order.
            var sortedResults = healthCheckResults.OrderBy(r => r.Data["name"]).ToList();
            healthCheckResults.Clear();

            if (!_testOldMapService)
            {
                Assert.AreEqual(1, sortedResults.Count);
                Assert.AreEqual(healthStatus, sortedResults[0].Status);
                Assert.AreEqual("", sortedResults[0].Data["name"]);
            }
            else
            {
                Assert.AreEqual(2, sortedResults.Count);
                Assert.AreEqual(healthStatus, sortedResults[0].Status);
                Assert.AreEqual("", sortedResults[0].Data["name"]);
                Assert.AreEqual(healthStatus, sortedResults[1].Status);
                Assert.AreEqual("filtered", sortedResults[1].Data["name"]);
            }
        }

        Task<HealthCheckResult> RunHealthCheck(HealthStatus status, string name)
        {
            Debug.Assert(healthCheckResults != null);

            var result = new HealthCheckResult(healthCheckStatus, $"Description: {name}", data: new Dictionary<string, object> { ["name"] = name });
            // Health checks are run in parallel. Lock to avoid thread safety issues.
            lock (healthCheckResults)
            {
                healthCheckResults.Add(result);
            }
            return Task.FromResult(result);
        }
    }

    [Test]
    public async Task HealthService_CheckWithFilter_UsePublishedChecks_FilteredResultsExcluded()
    {
        // Arrange
        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddGrpcHealthChecks(o =>
            {
                Map(o.Services, "", (name, tags) => !tags.Contains("exclude"));
                o.UseHealthChecksCache = true;
            })
            .AddAsyncCheck("", () => Task.FromResult(healthCheckResult))
            .AddAsyncCheck("filtered", () => Task.FromResult(healthCheckResult), new string[] { "exclude" });
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        HealthReport? report = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testPublisher = new TestHealthCheckPublisher();
        testPublisher.OnHealthReport = async r =>
        {
            report = r;
            await syncPoint.WaitToContinue();
        };
        services.AddSingleton<IHealthCheckPublisher>(testPublisher);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        async Task CheckForStatusAsync(string service, HealthCheckResponse.Types.ServingStatus status)
        {
            var context = new TestServerCallContext(DateTime.MaxValue, CancellationToken.None);

            var result = await healthService!.Check(new HealthCheckRequest() { Service = service }, context);

            Assert.AreEqual(status, result.Status);
        }

        await ExceptionAssert.ThrowsAsync<RpcException>(() => CheckForStatusAsync(service: "", HealthCheckResponse.Types.ServingStatus.ServiceUnknown));
        await ExceptionAssert.ThrowsAsync<RpcException>(() => CheckForStatusAsync(service: "filtered", HealthCheckResponse.Types.ServingStatus.ServiceUnknown));

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthStatus.Healthy, report!.Status);
            syncPoint.Continue();
            await CheckForStatusAsync(service: "", HealthCheckResponse.Types.ServingStatus.Serving);

            healthCheckResult = new HealthCheckResult(HealthStatus.Unhealthy);
            syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthStatus.Unhealthy, report!.Status);
            syncPoint.Continue();
            await CheckForStatusAsync(service: "", HealthCheckResponse.Types.ServingStatus.NotServing);

            await ExceptionAssert.ThrowsAsync<RpcException>(() => CheckForStatusAsync(service: "filtered", HealthCheckResponse.Types.ServingStatus.ServiceUnknown));
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task HealthService_RemoveDefault_DefaultNotFound()
    {
        // Arrange
        var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

        var services = new ServiceCollection();
        services.AddLogging();
        services
            .AddGrpcHealthChecks(o =>
            {
                o.Services.Clear();
                Map(o.Services, "new", (_, __) => true);
            })
            .AddAsyncCheck("", () => Task.FromResult(healthCheckResult));
        services.Configure<HealthCheckPublisherOptions>(o =>
        {
            o.Delay = TimeSpan.FromSeconds(1);
            o.Period = TimeSpan.FromSeconds(1);
        });
        var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);

        HealthReport? report = null;
        var syncPoint = new SyncPoint(runContinuationsAsynchronously: true);
        var testPublisher = new TestHealthCheckPublisher();
        testPublisher.OnHealthReport = async r =>
        {
            report = r;
            await syncPoint.WaitToContinue();
        };
        services.AddSingleton<IHealthCheckPublisher>(testPublisher);

        var serviceProvider = services.BuildServiceProvider();

        var healthService = CreateService(serviceProvider);
        var hostedService = serviceProvider.GetServices<IHostedService>().Single();

        async Task CheckForStatusAsync(string service, HealthCheckResponse.Types.ServingStatus status)
        {
            var context = new TestServerCallContext(DateTime.MaxValue, CancellationToken.None);

            var result = await healthService!.Check(new HealthCheckRequest() { Service = service }, context);

            Assert.AreEqual(status, result.Status);
        }

        // Act
        await hostedService.StartAsync(CancellationToken.None);

        // Assert
        try
        {
            await syncPoint.WaitForSyncPoint().DefaultTimeout();
            Assert.AreEqual(HealthStatus.Healthy, report!.Status);
            syncPoint.Continue();

            await ExceptionAssert.ThrowsAsync<RpcException>(() => CheckForStatusAsync(service: "", HealthCheckResponse.Types.ServingStatus.ServiceUnknown));
            
            await CheckForStatusAsync(service: "new", HealthCheckResponse.Types.ServingStatus.Serving);
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
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
