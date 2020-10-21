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
    internal abstract class ServerCallDeadlineManager : IAsyncDisposable
    {
        private CancellationTokenSource? _deadlineCts;
        private Task? _deadlineExceededTask;
        private CancellationTokenRegistration _deadlineExceededRegistration;
        private CancellationTokenRegistration _requestAbortedRegistration;
        private readonly TaskCompletionSource<object?> _cancellationProcessedTcs;
        protected HttpContextServerCallContext ServerCallContext { get; }

        internal DateTime Deadline { get; private set; }
        // Lock is to ensure deadline doesn't execute as call is completing
        internal SemaphoreSlim Lock { get; }
        // Internal for testing
        public bool CallComplete { get; private set; }

        public CancellationToken CancellationToken => _deadlineCts!.Token;

        // Task to wait for when a call is being completed to ensure that registered deadline cancellation
        // events have finished processing.
        // - Avoids a race condition between deadline being raised and the call completing.
        // - Required because OCE error thrown from token happens before registered events.
        public Task CancellationProcessedTask => _cancellationProcessedTcs.Task;

        public static ServerCallDeadlineManager Create(
            HttpContextServerCallContext serverCallContext,
            ISystemClock clock,
            TimeSpan timeout,
            CancellationToken requestAborted)
        {
            ServerCallDeadlineManager serverCallDeadlineManager;
            if ((long)timeout.TotalMilliseconds <= int.MaxValue)
            {
                serverCallDeadlineManager = new DefaultServerCallDeadlineManager(serverCallContext);
            }
            else
            {
                // Deadline exceeds the maximum CancellationTokenSource delay.
                // In this situation we'll use a Timer instead.
                serverCallDeadlineManager = new LongTimeoutServerCallDeadlineManager(serverCallContext);
            }

            serverCallDeadlineManager.Initialize(clock, timeout, requestAborted);
            return serverCallDeadlineManager;
        }

        protected ServerCallDeadlineManager(HttpContextServerCallContext serverCallContext)
        {
            // Set fields that need to exist before setting up deadline CTS
            // Ensures callback can run successfully before CTS timer starts
            ServerCallContext = serverCallContext;

            Lock = new SemaphoreSlim(1, 1);
            _cancellationProcessedTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Initialize(ISystemClock clock, TimeSpan timeout, CancellationToken requestAborted)
        {
            Deadline = clock.UtcNow.Add(timeout);

            _deadlineCts = CreateCancellationTokenSource(timeout, clock);
            _deadlineExceededRegistration = _deadlineCts.Token.Register(DeadlineExceeded);

            _requestAbortedRegistration = requestAborted.Register(() =>
            {
                // Call is complete if the request has aborted
                CallComplete = true;
                _deadlineCts?.Cancel();
            });
        }

        protected abstract CancellationTokenSource CreateCancellationTokenSource(TimeSpan timeout, ISystemClock clock);

        public void SetCallEnded()
        {
            CallComplete = true;
        }

        protected void DeadlineExceeded()
        {
            _deadlineExceededTask = DeadlineExceededAsync();
        }

        private async Task DeadlineExceededAsync()
        {
            try
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

                    await ServerCallContext.DeadlineExceededAsync();
                    CallComplete = true;
                }
                finally
                {
                    Lock.Release();
                }
            }
            finally
            {
                _cancellationProcessedTcs.TrySetResult(null);
            }
        }

        private bool CanExceedDeadline()
        {
            // Deadline callback could be raised by the CTS after call has been completed (either successfully, with error, or aborted)
            // but before deadline exceeded registration has been disposed
            return !CallComplete;
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
                Dispose(true);
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

            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            Lock.Dispose();
            _deadlineCts!.Dispose();
            _requestAbortedRegistration.Dispose();
        }
    }
}
