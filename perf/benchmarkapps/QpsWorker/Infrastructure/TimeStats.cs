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
using System.Globalization;

// Copied from https://github.com/grpc/grpc/tree/master/src/csharp/Grpc.IntegrationTesting
namespace QpsWorker.Infrastructure;

/// <summary>
/// Snapshottable time statistics.
/// </summary>
public class TimeStats
{
    private readonly object _myLock = new object();
    private DateTime _lastWallClock;
    private TimeSpan _lastUserTime;
    private TimeSpan _lastPrivilegedTime;

    public TimeStats()
    {
        _lastWallClock = DateTime.UtcNow;
        _lastUserTime = Process.GetCurrentProcess().UserProcessorTime;
        _lastPrivilegedTime = Process.GetCurrentProcess().PrivilegedProcessorTime;
    }

    public Snapshot GetSnapshot(bool reset)
    {
        lock (_myLock)
        {
            var wallClock = DateTime.UtcNow;
            var userTime = Process.GetCurrentProcess().UserProcessorTime;
            var privilegedTime = Process.GetCurrentProcess().PrivilegedProcessorTime;
            var snapshot = new Snapshot(wallClock - _lastWallClock, userTime - _lastUserTime, privilegedTime - _lastPrivilegedTime);

            if (reset)
            {
                _lastWallClock = wallClock;
                _lastUserTime = userTime;
                _lastPrivilegedTime = privilegedTime;
            }
            return snapshot;
        }
    }

    public class Snapshot
    {
        public TimeSpan WallClockTime { get; }
        public TimeSpan UserProcessorTime { get; }
        public TimeSpan PrivilegedProcessorTime { get; }

        public Snapshot(TimeSpan wallClockTime, TimeSpan userProcessorTime, TimeSpan privilegedProcessorTime)
        {
            WallClockTime = wallClockTime;
            UserProcessorTime = userProcessorTime;
            PrivilegedProcessorTime = privilegedProcessorTime;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "[TimeStats.Snapshot: wallClock {0}, userProcessor {1}, privilegedProcessor {2}]", WallClockTime, UserProcessorTime, PrivilegedProcessorTime);
        }
    }
}
