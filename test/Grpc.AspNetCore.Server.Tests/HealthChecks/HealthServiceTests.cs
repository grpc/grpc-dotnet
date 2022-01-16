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

using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HealthChecks
{
    [TestFixture]
    public class HealthServiceTests
    {
        [Test]
        public async Task HealthService_Watch_WriteResults()
        {
            // Arrange
            var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpcHealthChecks().AddAsyncCheck(
                "",
                () => Task.FromResult(healthCheckResult), new string[] { "sample" });
            services.Configure<HealthCheckPublisherOptions>(o =>
            {
                o.Delay = TimeSpan.FromSeconds(1);
                o.Period = TimeSpan.FromSeconds(1);
            });

            var serviceProvider = services.BuildServiceProvider();

            var healthService = serviceProvider.GetRequiredService<HealthServiceImpl>();
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

        [Test]
        public async Task HealthService_CheckWithFilter_FilteredResultsExcluded()
        {
            // Arrange
            var healthCheckResult = new HealthCheckResult(HealthStatus.Healthy);

            var services = new ServiceCollection();
            services.AddLogging();
            services
                .AddGrpcHealthChecks(o =>
                {
                    o.Services.MapService("", result => !result.Tags.Contains("exclude"));
                })
                .AddAsyncCheck("", () => Task.FromResult(healthCheckResult))
                .AddAsyncCheck("filtered", () => Task.FromResult(healthCheckResult), new string[] { "exclude" });
            services.Configure<HealthCheckPublisherOptions>(o =>
            {
                o.Delay = TimeSpan.FromSeconds(1);
                o.Period = TimeSpan.FromSeconds(1);
            });

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

            var healthService = serviceProvider.GetRequiredService<HealthServiceImpl>();
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
                    o.Services.MapService("new", result => true);
                })
                .AddAsyncCheck("", () => Task.FromResult(healthCheckResult));
            services.Configure<HealthCheckPublisherOptions>(o =>
            {
                o.Delay = TimeSpan.FromSeconds(1);
                o.Period = TimeSpan.FromSeconds(1);
            });

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

            var healthService = serviceProvider.GetRequiredService<HealthServiceImpl>();
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
    }
}
