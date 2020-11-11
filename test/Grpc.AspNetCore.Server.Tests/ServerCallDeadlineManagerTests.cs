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
using System.Linq;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class ServerCallDeadlineManagerTests
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

            // Act
            var manager = new ServerCallDeadlineManager(context, SystemClock.Instance, timeout);

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

            // Act
            var manager = new ServerCallDeadlineManager(context, testSystemClock, timeout, maxTimerDueTime: 5);

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

        [Test]
        public async Task CancellationToken_ThrowExceptionInRegister()
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var testSystemClock = new TestSystemClock(DateTime.UtcNow);
            var timeout = TimeSpan.FromMilliseconds(100);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.TimeoutHeader] = "100m";
            var context = CreateServerCallContext(httpContext, testLogger);
            var exception = new InvalidOperationException("Test");

            // Act
            var manager = new ServerCallDeadlineManager(context, SystemClock.Instance, timeout);

            // Assert
            manager.CancellationToken.Register(() =>
            {
                throw exception;
            });

            await TestHelpers.AssertIsTrueRetryAsync(
              () => context.Status.StatusCode == StatusCode.DeadlineExceeded,
              "StatusCode not set to DeadlineExceeded.").DefaultTimeout();

            await manager.WaitDeadlineCompleteAsync().DefaultTimeout();

            var deadlineExceededWrite = testSink.Writes.Single(w => w.EventId.Name == "DeadlineExceeded");
            Assert.AreEqual("Request with timeout of 00:00:00.1000000 has exceeded its deadline.", deadlineExceededWrite.Message);

            var deadlineCancellationErrorWrite = testSink.Writes.Single(w => w.EventId.Name == "DeadlineCancellationError");
            Assert.AreEqual("Error occurred while trying to cancel the request due to deadline exceeded.", deadlineCancellationErrorWrite.Message);
            Assert.AreEqual(exception, deadlineCancellationErrorWrite.Exception.InnerException);
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
