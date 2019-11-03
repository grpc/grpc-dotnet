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
using System.Threading.Tasks;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class ListenerSubscription : IDisposable
    {
        private readonly TestEventListener _testEventListener;
        private TaskCompletionSource<object?> _tcs;

        public ListenerSubscription(TestEventListener testEventListener, string counterName, long expectedValue)
        {
            _tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _testEventListener = testEventListener;
            CounterName = counterName;
            ExpectedValue = expectedValue;
        }

        public Task Task => _tcs.Task;

        public string CounterName { get; }
        public long ExpectedValue { get; }

        // Set the last value encountered for debugging purposes
        public long? LastValue { get; internal set; }

        public void Dispose()
        {
            _testEventListener.Unsubscribe(this);
        }

        internal void SetMatched()
        {
            _tcs.TrySetResult(null);
        }
    }
}
