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

#if NET5_0_OR_GREATER

using System;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Balancer;
using Grpc.Net.Client.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Balancer
{
    [TestFixture]
    public class DnsResolverTests : FunctionalTestBase
    {
        [Test]
        public async Task RefreshAsync_HasStarted_HasResult()
        {
            // Arranged
            ResolverResult? result = null;

            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                result = r;
            });

            // Act
            await dnsResolver.RefreshAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result!.Addresses!.Count, 0);
        }

        [Test]
        public async Task Start_IntervalSet_MultipleCallbacks()
        {
            // Arrange
            const int waitForCallCount = 10;
            var currentCallCount = 0;
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var testSystemClock = new TestSystemClock(DateTime.UtcNow);

            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, TimeSpan.FromSeconds(0.05));
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
                if (writeContext.State.ToString() == "Error querying DNS hosts for 'dns://localhost/'." &&
                    writeContext.Exception!.Message == "Resolver address 'dns://localhost/' doesn't have a path.")
                {
                    return true;
                }

                return false;
            });

            ResolverResult? result = null;

            var dnsResolver = new DnsResolver(new Uri("dns://localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                result = r;
            });

            // Act
            await dnsResolver.RefreshAsync(CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.Null(result!.Addresses);
            Assert.AreEqual(StatusCode.Unavailable, result!.Status.StatusCode);
            Assert.AreEqual("Error getting DNS hosts for address 'dns://localhost/'. InvalidOperationException: Resolver address 'dns://localhost/' doesn't have a path.", result!.Status.Detail);
            Assert.AreEqual("Resolver address 'dns://localhost/' doesn't have a path.", result!.Status.DebugException.Message);
        }

        [Test]
        public async Task RefreshAsync_MultipleCalls_HitRateLimit()
        {
            // Arrange
            ResolverResult? result = null;

            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                result = r;
            });

            // Act
            await dnsResolver.RefreshAsync(CancellationToken.None).DefaultTimeout();

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result!.Addresses!.Count, 0);

            var refreshTask1 = dnsResolver.RefreshAsync(CancellationToken.None);

            var completedTask = await Task.WhenAny(refreshTask1, Task.Delay(TimeSpan.FromSeconds(0.5))).DefaultTimeout();
            if (completedTask == refreshTask1)
            {
                Assert.Fail("Expected refresh to be delayed.");
            }

            // Refresh is already in progress so existing task is returned.
            var refreshTask2 = dnsResolver.RefreshAsync(CancellationToken.None);
            Assert.AreSame(refreshTask1, refreshTask2);
        }

        [Test]
        public async Task RefreshAsync_MultipleCallsThenCancellation_CallCanceled()
        {
            SetExpectedErrorsFilter(writeContext =>
            {
                if (writeContext.State.ToString() == "Error querying DNS hosts for 'dns:///localhost'." &&
                    writeContext.Exception!.Message == "A task was canceled.")
                {
                    return true;
                }

                return false;
            });

            // Arrange
            ResolverResult? result = null;

            var cts = new CancellationTokenSource();
            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                result = r;
            });

            // Act
            await dnsResolver.RefreshAsync(cts.Token).DefaultTimeout();

            // Assert
            Assert.NotNull(result);
            Assert.Greater(result!.Addresses!.Count, 0);

            var refreshTask = dnsResolver.RefreshAsync(cts.Token);

            cts.Cancel();

            await refreshTask.DefaultTimeout();

            Assert.NotNull(result);
            Assert.Null(result!.Addresses);
            Assert.AreEqual(StatusCode.Unavailable, result!.Status.StatusCode);
            Assert.AreEqual("Error getting DNS hosts for address 'dns:///localhost'. TaskCanceledException: A task was canceled.", result!.Status.Detail);
            Assert.AreEqual("A task was canceled.", result!.Status.DebugException.Message);
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