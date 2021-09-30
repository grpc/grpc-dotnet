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

using Grpc.Shared;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.Threading;

namespace Grpc.Net.ClientFactory.Internal
{
    // Thread-safety: We treat this class as immutable except for the timer. Creating a new object
    // for the 'expiry' pool simplifies the threading requirements significantly.
    internal sealed class ActiveChannelTrackingEntry
    {
        private static readonly TimerCallback _timerCallback = (s) => ((ActiveChannelTrackingEntry)s!).Timer_Tick();
        private readonly object _lock;
        private bool _timerInitialized;
        private Timer? _timer;
        private TimerCallback? _callback;

        public ActiveChannelTrackingEntry(
            EntryKey key,
            LifetimeTrackingCallInvoker callInvoker,
            IServiceScope? scope,
            TimeSpan lifetime)
        {
            Key = key;
            CallInvoker = callInvoker;
            Scope = scope;
            Lifetime = lifetime;

            _lock = new object();
        }

        public LifetimeTrackingCallInvoker CallInvoker { get; private set; }

        public TimeSpan Lifetime { get; }

        public EntryKey Key { get; }

        public IServiceScope? Scope { get; }

        public void StartExpiryTimer(TimerCallback callback)
        {
            if (Lifetime == Timeout.InfiniteTimeSpan)
            {
                return; // never expires.
            }

            if (Volatile.Read(ref _timerInitialized))
            {
                return;
            }

            StartExpiryTimerSlow(callback);
        }

        private void StartExpiryTimerSlow(TimerCallback callback)
        {
            Debug.Assert(Lifetime != Timeout.InfiniteTimeSpan);

            lock (_lock)
            {
                if (Volatile.Read(ref _timerInitialized))
                {
                    return;
                }

                _callback = callback;
                _timer = NonCapturingTimer.Create(_timerCallback, this, Lifetime, Timeout.InfiniteTimeSpan);
                _timerInitialized = true;
            }
        }

        private void Timer_Tick()
        {
            lock (_lock)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;

                    _callback!.Invoke(this);
                }
            }
        }
    }
}
