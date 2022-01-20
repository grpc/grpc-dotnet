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

#if SUPPORT_LOAD_BALANCING

using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Internal;
using Grpc.Tests.Shared;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer
{
    [TestFixture]
    public class DnsResolverTests : FunctionalTestBase
    {
        [Test]
        public async Task Refresh_HasStarted_HasResult()
        {
            // Arranged
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var dnsResolver = CreateDnsResolver(new Uri("dns:///localhost"));
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.Greater(result.Addresses!.Count, 0);
        }

        private DnsResolver CreateDnsResolver(Uri address, int? defaultPort = null, TimeSpan? refreshInterval = null)
        {
            return new DnsResolver(address, defaultPort ?? 80, LoggerFactory, refreshInterval ?? Timeout.InfiniteTimeSpan);
        }

        [Test]
        public async Task Start_IntervalSet_MultipleCallbacks()
        {
            // Arrange
            const int waitForCallCount = 10;
            var currentCallCount = 0;
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);

            var dnsResolver = CreateDnsResolver(new Uri("dns:///localhost"), refreshInterval: TimeSpan.FromSeconds(0.05));
            dnsResolver.SystemClock = testSystemClock;

            // Act
            dnsResolver.Start(r =>
            {
                // Avoid rate limit.
                testSystemClock.UtcNow = testSystemClock.UtcNow + TimeSpan.FromSeconds(20);

                if (Interlocked.Increment(ref currentCallCount) >= waitForCallCount)
                {
                    tcs.SetResult(r);
                }
            });

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.NotNull(result);
            Assert.Greater(result!.Addresses!.Count, 0);
        }

        [Test]
        public async Task RefreshAsync_NoAddressPath_Error()
        {
            // Arrange
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.State.ToString() == "Error querying DNS hosts for ''." &&
                    writeContext.Exception!.Message == "Resolver address 'dns://localhost/' is not valid. Please use dns:/// for DNS provider.")
                {
                    return true;
                }

                return false;
            });

            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = CreateDnsResolver(address: new Uri("dns://localhost"));
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();

            Assert.AreEqual(StatusCode.Unavailable, result.Status.StatusCode);
            Assert.AreEqual("Error getting DNS hosts for address ''. InvalidOperationException: Resolver address 'dns://localhost/' is not valid. Please use dns:/// for DNS provider.", result!.Status.Detail);
            Assert.AreEqual("Resolver address 'dns://localhost/' is not valid. Please use dns:/// for DNS provider.", result!.Status.DebugException!.Message);
        }

        [Test]
        public async Task RefreshAsync_MultipleCalls_HitRateLimit()
        {
            // Arrange
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = CreateDnsResolver(new Uri("dns:///localhost"));
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.Greater(result.Addresses!.Count, 0);

            // Wait for the internal resolve task to be completed before triggering refresh again
            await dnsResolver._resolveTask.DefaultTimeout();
            tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            dnsResolver.Refresh();

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(0.5))).DefaultTimeout();
            if (completedTask == tcs.Task)
            {
                Assert.Fail("Expected refresh to be delayed.");
            }
        }

        [Test]
        public async Task RefreshAsync_MultipleCallsThenCancellation_CallCanceled()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.State.ToString() == "Error querying DNS hosts for 'localhost'." &&
                    writeContext.Exception!.Message == "A task was canceled.")
                {
                    return true;
                }

                return false;
            });

            // Arrange
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = CreateDnsResolver(new Uri("dns:///localhost"));
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.Greater(result.Addresses!.Count, 0);

            // Wait for the internal resolve task to be completed before triggering refresh again
            await dnsResolver._resolveTask.DefaultTimeout();
            tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            dnsResolver.Refresh();

            dnsResolver.Dispose();

            result = await tcs.Task.DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, result.Status.StatusCode);
            Assert.AreEqual("Error getting DNS hosts for address 'localhost'. TaskCanceledException: A task was canceled.", result.Status.Detail);
            Assert.AreEqual("A task was canceled.", result.Status.DebugException!.Message);
        }

        [Test]
        public async Task DNS_Port_Works()
        {
            const int defaultPort = 1234; // Will be ignored because port is specified in address
            const int specifiedPort = 8080;

            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = CreateDnsResolver(new Uri($"dns:///localhost:{specifiedPort}"), defaultPort);
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            Assert.False(HasLogException((ex) =>
            {
                return ex is SocketException;
            }));

            var result = await tcs.Task.DefaultTimeout();
            Assert.AreEqual(StatusCode.OK, result.Status.StatusCode);

            var addresses = result.Addresses!;
            Logger.LogInformation($"Got {addresses.Count} addresses.");

            // Note: addresses returned from localhost depend on operating system.
            // Validate whatever we get back has the specified port.
            for (var i = 0; i < addresses.Count; i++)
            {
                var address = addresses[i];

                Logger.LogInformation($"Address: {address}");
                Assert.AreEqual(specifiedPort, address.EndPoint.Port);
            }
        }

        [Test]
        public async Task DefaultPort_Localhost_AddressesHaveDefaultPort()
        {
            const int defaultPort = 8081;

            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = CreateDnsResolver(new Uri("dns:///localhost"), defaultPort);
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.AreEqual(StatusCode.OK, result.Status.StatusCode);

            var addresses = result.Addresses!;
            Logger.LogInformation($"Got {addresses.Count} addresses.");

            // Note: addresses returned from localhost depend on operating system.
            // Validate whatever we get back has the specified port.
            for (var i = 0; i < addresses.Count; i++)
            {
                var address = addresses[i];

                Logger.LogInformation($"Address: {address}");
                Assert.AreEqual(defaultPort, address.EndPoint.Port);
            }
        }

        private class TestSystemClock : ISystemClock
        {
            public TestSystemClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; set; }
        }
    }
}

#endif