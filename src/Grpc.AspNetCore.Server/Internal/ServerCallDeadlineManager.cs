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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class ServerCallDeadlineManager : IAsyncDisposable
    {
        private CancellationTokenSource _deadlineCts;
        private Task? _deadlineExceededTask;
        private CancellationTokenRegistration _deadlineExceededRegistration;
        private CancellationTokenRegistration _requestAbortedRegistration;
        private Func<Task> _deadlineExceededCallback;

        internal DateTime Deadline { get; private set; }
        // Lock is to ensure deadline doesn't execute as call is completing
        internal SemaphoreSlim Lock { get; private set; }
        // Internal for testing
        internal bool _callComplete;

        public CancellationToken CancellationToken => _deadlineCts.Token;

        public ServerCallDeadlineManager(ISystemClock clock, TimeSpan timeout, Func<Task> deadlineExceededCallback, CancellationToken requestAborted)
        {
            Deadline = clock.UtcNow.Add(timeout);

            // Set fields that need to exist before setting up deadline CTS
            // Ensures callback can run successfully before CTS timer starts
            _deadlineExceededCallback = deadlineExceededCallback;
            Lock = new SemaphoreSlim(1, 1);

            _deadlineCts = new CancellationTokenSource(timeout);
            _deadlineExceededRegistration = _deadlineCts.Token.Register(DeadlineExceeded);
            _requestAbortedRegistration = requestAborted.Register(() =>
            {
                // Call is complete if the request has aborted
                _callComplete = true;
                _deadlineCts?.Cancel();
            });
        }

        public void SetCallComplete()
        {
            _callComplete = true;
        }

        private void DeadlineExceeded()
        {
            _deadlineExceededTask = DeadlineExceededAsync();
        }

        private async Task DeadlineExceededAsync()
        {
            if (!CanExceedDeadline())
            {
                return;
            }

            Debug.Assert(Lock != null, "Lock has not been created.");

            await Lock.WaitAsync();

            try
            {
                // Double check after lock is aquired
                if (!CanExceedDeadline())
                {
                    return;
                }

                await _deadlineExceededCallback();
            }
            finally
            {
                Lock.Release();
            }
        }

        private bool CanExceedDeadline()
        {
            // Deadline callback could be raised by the CTS after call has been completed (either successfully, with error, or aborted)
            // but before deadline exceeded registration has been disposed
            return !_callComplete;
        }

        public ValueTask DisposeAsync()
        {
            // Deadline registration needs to be disposed with DisposeAsync, and the task completed
            // before the lock can be disposed.
            // Awaiting deadline registration and deadline task ensures it has finished running, so there is
            // no way for deadline logic to attempt to wait on a disposed lock.
            var disposeTask = _deadlineExceededRegistration.DisposeAsync();

            if (disposeTask.IsCompletedSuccessfully &&
                (_deadlineExceededTask == null || _deadlineExceededTask.IsCompletedSuccessfully))
            {
                DisposeCore();
                return default;
            }

            return DeadlineDisposeAsyncCore(disposeTask);
        }

        private async ValueTask DeadlineDisposeAsyncCore(ValueTask disposeTask)
        {
            await disposeTask;
            if (_deadlineExceededTask != null)
            {
                await _deadlineExceededTask;
            }

            DisposeCore();
        }

        private void DisposeCore()
        {
            Lock!.Dispose();
            _deadlineCts!.Dispose();
            _requestAbortedRegistration.Dispose();
        }
    }
}
