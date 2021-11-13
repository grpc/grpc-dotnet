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
        public async Task Refresh_HasStarted_HasResult()
        {
            // Arranged
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
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

            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = new DnsResolver(new Uri("dns://localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();

            Assert.AreEqual(StatusCode.Unavailable, result.Status.StatusCode);
            Assert.AreEqual("Error getting DNS hosts for address 'dns://localhost/'. InvalidOperationException: Resolver address 'dns://localhost/' doesn't have a path.", result.Status.Detail);
            Assert.AreEqual("Resolver address 'dns://localhost/' doesn't have a path.", result.Status.DebugException.Message);
        }

        [Test]
        public async Task RefreshAsync_MultipleCalls_HitRateLimit()
        {
            // Arrange
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.Greater(result.Addresses!.Count, 0);

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
                if (writeContext.State.ToString() == "Error querying DNS hosts for 'dns:///localhost'." &&
                    writeContext.Exception!.Message == "A task was canceled.")
                {
                    return true;
                }

                return false;
            });

            // Arrange
            var tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cts = new CancellationTokenSource();
            var dnsResolver = new DnsResolver(new Uri("dns:///localhost"), LoggerFactory, Timeout.InfiniteTimeSpan);
            dnsResolver.Start(r =>
            {
                tcs.SetResult(r);
            });

            // Act
            dnsResolver.Refresh();

            // Assert
            var result = await tcs.Task.DefaultTimeout();
            Assert.Greater(result.Addresses!.Count, 0);

            tcs = new TaskCompletionSource<ResolverResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            dnsResolver.Refresh();

            dnsResolver.Dispose();

            result = await tcs.Task.DefaultTimeout();
            Assert.AreEqual(StatusCode.Unavailable, result.Status.StatusCode);
            Assert.AreEqual("Error getting DNS hosts for address 'dns:///localhost'. TaskCanceledException: A task was canceled.", result.Status.Detail);
            Assert.AreEqual("A task was canceled.", result.Status.DebugException.Message);
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