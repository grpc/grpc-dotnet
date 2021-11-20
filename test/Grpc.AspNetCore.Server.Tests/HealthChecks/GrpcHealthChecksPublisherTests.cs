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
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Reflection
{
    [TestFixture]
    public class GrpcHealthChecksPublisherTests
    {
        [Test]
        public async Task PublishAsync_Check_ChangingStatus()
        {
            // Arrange
            var healthService = new HealthServiceImpl();
            var publisher = new GrpcHealthChecksPublisher(healthService);

            HealthCheckResponse response;

            // Act 1
            var ex = await ExceptionAssert.ThrowsAsync<RpcException>(() => healthService.Check(new HealthCheckRequest { Service = "" }, context: null));

            // Assert 1
            Assert.AreEqual(StatusCode.NotFound, ex.StatusCode);

            // Act 2
            var report = CreateSimpleHealthReport(HealthStatus.Healthy);
            await publisher.PublishAsync(report, CancellationToken.None);

            response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null);

            // Assert 2
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

            // Act 3
            report = CreateSimpleHealthReport(HealthStatus.Unhealthy);
            await publisher.PublishAsync(report, CancellationToken.None);

            response = await healthService.Check(new HealthCheckRequest { Service = "" }, context: null);

            // Act 3
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response.Status);
        }

        private static HealthReport CreateSimpleHealthReport(HealthStatus healthStatus)
        {
            return new HealthReport(
                new Dictionary<string, HealthReportEntry>
                {
                    [""] = new HealthReportEntry(healthStatus, "Description!", TimeSpan.Zero, exception: null, data: null)
                },
                TimeSpan.Zero);
        }

        [Test]
        public async Task PublishAsync_Check_MapStatuses()
        {
            // Arrange
            var healthService = new HealthServiceImpl();
            var publisher = new GrpcHealthChecksPublisher(healthService);

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
            response = await healthService.Check(new HealthCheckRequest { Service = nameof(HealthStatus.Healthy) }, context: null);
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

            response = await healthService.Check(new HealthCheckRequest { Service = nameof(HealthStatus.Degraded) }, context: null);
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);

            response = await healthService.Check(new HealthCheckRequest { Service = nameof(HealthStatus.Unhealthy) }, context: null);
            Assert.AreEqual(HealthCheckResponse.Types.ServingStatus.NotServing, response.Status);
        }

        [Test]
        public async Task PublishAsync_Watch_ChangingStatus()
        {
            // Arrange
            var healthService = new HealthServiceImpl();
            var publisher = new GrpcHealthChecksPublisher(healthService);
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
    }
}
