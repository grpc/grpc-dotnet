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
using System.Threading;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class LongTimeoutServerCallDeadlineManager : ServerCallDeadlineManager
    {
        private Timer? _longDeadlineTimer;
        private ISystemClock? _systemClock;
        // Internal for unit testing
        internal long MaxTimerDueTime = uint.MaxValue - 1; // Max System.Threading.Timer due time

        public LongTimeoutServerCallDeadlineManager(HttpContextServerCallContext serverCallContext) : base(serverCallContext)
        {
        }

        protected override CancellationTokenSource CreateCancellationTokenSource(TimeSpan timeout, ISystemClock clock)
        {
            _systemClock = clock;
            _longDeadlineTimer = new Timer(DeadlineExceededCallback, null, GetTimerDueTime(timeout), Timeout.Infinite);

            return new CancellationTokenSource();
        }

        private void DeadlineExceededCallback(object? state)
        {
            var remaining = Deadline - _systemClock!.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                DeadlineExceeded();
            }
            else
            {
                // Deadline has not been reached because timer maximum due time was smaller than deadline.
                // Reschedule DeadlineExceeded again until deadline has been exceeded.
                GrpcServerLog.DeadlineTimerRescheduled(ServerCallContext.Logger, remaining);

                _longDeadlineTimer!.Change(GetTimerDueTime(remaining), Timeout.Infinite);
            }
        }

        private long GetTimerDueTime(TimeSpan timeout)
        {
            // Timer has a maximum allowed due time.
            // The called method will rechedule the timer if the deadline time has not passed.
            var dueTimeMilliseconds = timeout.Ticks / TimeSpan.TicksPerMillisecond;
            dueTimeMilliseconds = Math.Min(dueTimeMilliseconds, MaxTimerDueTime);
            // Timer can't have a negative due time
            dueTimeMilliseconds = Math.Max(dueTimeMilliseconds, 0);

            return dueTimeMilliseconds;
        }
    }
}
