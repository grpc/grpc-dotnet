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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class LongTimeoutServerCallDeadlineManagerTests
    {
        [Test]
        public async Task SmallDeadline_DeadlineExceededWithoutReschedule()
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var timeout = TimeSpan.FromMilliseconds(100);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "100m";
            var context = CreateServerCallContext(httpContext, testLogger);

            var manager = new LongTimeoutServerCallDeadlineManager(context);

            // Act
            manager.Initialize(testSystemClock, timeout, CancellationToken.None);

            // Assert
            var assertTask = TestHelpers.AssertIsTrueRetryAsync(
              () => context.Status.StatusCode == StatusCode.DeadlineExceeded,
              "StatusCode not set to DeadlineExceeded.");

            testSystemClock.UtcNow = testSystemClock.UtcNow.Add(timeout);

            await assertTask.DefaultTimeout();

            var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
            Assert.AreEqual("Request with timeout of 00:00:00.1000000 has exceeded its deadline.", write.Message);

            Assert.IsFalse(testSink.Writes.Any(w => w.EventId.Name == "DeadlineTimerRescheduled"));
        }

        [Test]
        public async Task LargeDeadline_DeadlineExceededWithReschedule()
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var timeout = TimeSpan.FromMilliseconds(100);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "100m";
            var context = CreateServerCallContext(httpContext, testLogger);

            var manager = new LongTimeoutServerCallDeadlineManager(context);
            manager.MaxTimerDueTime = 5;

            // Act
            manager.Initialize(testSystemClock, timeout, CancellationToken.None);

            // Assert
            var assertTask = TestHelpers.AssertIsTrueRetryAsync(
              () => context.Status.StatusCode == StatusCode.DeadlineExceeded,
              "StatusCode not set to DeadlineExceeded.");

            await Task.Delay(timeout);
            testSystemClock.UtcNow = testSystemClock.UtcNow.Add(timeout);

            await assertTask.DefaultTimeout();

            var write = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
            Assert.AreEqual("Request with timeout of 00:00:00.1000000 has exceeded its deadline.", write.Message);

            Assert.IsTrue(testSink.Writes.Any(w => w.EventId.Name == "DeadlineTimerRescheduled"));
        }

        private HttpContextServerCallContext CreateServerCallContext(HttpContext httpContext, ILogger? logger = null)
        {
            return HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext: httpContext,
                logger: logger,
                initialize: false);
        }
    }
}
