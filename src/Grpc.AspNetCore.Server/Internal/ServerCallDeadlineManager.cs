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
using Grpc.Shared;

namespace Grpc.AspNetCore.Server.Internal;

internal sealed class ServerCallDeadlineManager : IAsyncDisposable
{
    // Max System.Threading.Timer due time
    private const long DefaultMaxTimerDueTime = uint.MaxValue - 1;

    // Avoid allocating delegates
    private static readonly TimerCallback DeadlineExceededDelegate = DeadlineExceededCallback;
    private static readonly TimerCallback DeadlineExceededLongDelegate = DeadlineExceededLongCallback;

    private readonly Timer _longDeadlineTimer;
    private readonly ISystemClock _systemClock;
    private readonly HttpContextServerCallContext _serverCallContext;

    private CancellationTokenSource? _deadlineCts;
    private CancellationTokenRegistration _requestAbortedRegistration;
    private TaskCompletionSource<object?>? _deadlineExceededCompleteTcs;

    public DateTime Deadline { get; }
    public bool IsCallComplete { get; private set; }
    public bool IsDeadlineExceededStarted => _deadlineExceededCompleteTcs != null;

    // Accessed by developers via ServerCallContext.CancellationToken
    public CancellationToken CancellationToken
    {
        get
        {
            // Lazy create a CT only when requested for performance
            if (_deadlineCts == null)
            {
                lock (this)
                {
                    // Double check locking
                    if (_deadlineCts == null)
                    {
                        _deadlineCts = new CancellationTokenSource();
                        if (IsDeadlineExceededStarted && IsCallComplete)
                        {
                            // If deadline has started exceeding and it has finished then the token can be immediately cancelled
                            _deadlineCts.Cancel();
                        }
                        else
                        {
                            // Deadline CT should be cancelled if the request is aborted
                            _requestAbortedRegistration = _serverCallContext.HttpContext.RequestAborted.Register(RequestAborted);
                        }
                    }
                }
            }

            return _deadlineCts.Token;
        }
    }

    public ServerCallDeadlineManager(HttpContextServerCallContext serverCallContext, ISystemClock clock, TimeSpan timeout, long maxTimerDueTime = DefaultMaxTimerDueTime)
    {
        // Set fields that need to exist before setting up deadline CTS
        // Ensures callback can run successfully before CTS timer starts
        _serverCallContext = serverCallContext;

        Deadline = clock.UtcNow.Add(timeout);

        _systemClock = clock;

        var timerMilliseconds = CommonGrpcProtocolHelpers.GetTimerDueTime(timeout, maxTimerDueTime);
        if (timerMilliseconds == maxTimerDueTime)
        {
            // Create timer and set to field before setting time.
            // Ensures there is no weird situation where the timer triggers
            // before the field is set. Shouldn't happen because only long deadlines
            // will take this path but better to be safe than sorry.
            _longDeadlineTimer = NonCapturingTimer.Create(DeadlineExceededLongDelegate, (this, maxTimerDueTime), Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _longDeadlineTimer.Change(timerMilliseconds, Timeout.Infinite);
        }
        else
        {
            _longDeadlineTimer = NonCapturingTimer.Create(DeadlineExceededDelegate, this, TimeSpan.FromMilliseconds(timerMilliseconds), Timeout.InfiniteTimeSpan);
        }
    }

    // Task to wait for when a call is being completed to ensure that registered deadline cancellation
    // events have finished processing.
    // - Avoids a race condition between deadline being raised and the call completing.
    // - Required because OCE error thrown from token happens before registered events.
    public Task WaitDeadlineCompleteAsync()
    {
        Debug.Assert(_deadlineExceededCompleteTcs != null, "Can only be called if deadline is started.");

        return _deadlineExceededCompleteTcs.Task;
    }

    private static void DeadlineExceededCallback(object? state) => _ = ((ServerCallDeadlineManager)state!).DeadlineExceededAsync();

    private static void DeadlineExceededLongCallback(object? state)
    {
        var (manager, maxTimerDueTime) = (ValueTuple<ServerCallDeadlineManager, long>)state!;
        var remaining = manager.Deadline - manager._systemClock.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _ = manager.DeadlineExceededAsync();
        }
        else
        {
            // Deadline has not been reached because timer maximum due time was smaller than deadline.
            // Reschedule DeadlineExceeded again until deadline has been exceeded.
            GrpcServerLog.DeadlineTimerRescheduled(manager._serverCallContext.Logger, remaining);

            manager._longDeadlineTimer.Change(CommonGrpcProtocolHelpers.GetTimerDueTime(remaining, maxTimerDueTime), Timeout.Infinite);
        }
    }

    private void RequestAborted()
    {
        // Call is complete if the request has aborted
        lock (this)
        {
            IsCallComplete = true;
        }

        // Doesn't matter if error from Cancel throws. Canceller of request aborted will handle exception.
        Debug.Assert(_deadlineCts != null, "Deadline CTS is created when request aborted method is registered.");
        _deadlineCts.Cancel();
    }

    public void SetCallEnded()
    {
        lock (this)
        {
            IsCallComplete = true;
        }
    }

    public bool TrySetCallComplete()
    {
        lock (this)
        {
            if (!IsDeadlineExceededStarted)
            {
                IsCallComplete = true;
                return true;
            }

            return false;
        }
    }

    private async Task DeadlineExceededAsync()
    {
        if (!TryStartExceededDeadline())
        {
            return;
        }

        try
        {
            await _serverCallContext.DeadlineExceededAsync();
            lock (this)
            {
                IsCallComplete = true;
            }

            // Canceling CTS will trigger registered callbacks.
            // Exception could be thrown from them.
            _deadlineCts?.Cancel();
        }
        catch (Exception ex)
        {
            GrpcServerLog.DeadlineCancellationError(_serverCallContext.Logger, ex);
        }
        finally
        {
            _deadlineExceededCompleteTcs!.TrySetResult(null);
        }
    }

    private bool TryStartExceededDeadline()
    {
        lock (this)
        {
            // Deadline callback could be raised by the CTS after call has been completed (either successfully, with error, or aborted)
            // but before deadline exceeded registration has been disposed
            if (!IsCallComplete)
            {
                _deadlineExceededCompleteTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            }

            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        // Timer.DisposeAsync will wait until any in-progress callbacks are complete.
        // By itself, this isn't enough to ensure the deadline has finished being raised because
        // the callback doesn't return Task. _deadlineExceededCompleteTcs must also be awaited
        // (it is set when a deadline starts being raised) to ensure the deadline manager is finished
        // and resources can be disposed.

        var disposeTask = _longDeadlineTimer.DisposeAsync();

        if (disposeTask.IsCompletedSuccessfully &&
            (_deadlineExceededCompleteTcs == null || _deadlineExceededCompleteTcs.Task.IsCompletedSuccessfully))
        {
            // Fast-path to avoid async state machine.
            DisposeCore();
            return default;
        }

        return DeadlineDisposeAsyncCore(disposeTask);
    }

    private async ValueTask DeadlineDisposeAsyncCore(ValueTask disposeTask)
    {
        await disposeTask;
        // Ensure an in-progress deadline is finished before disposing.
        // Need to await to avoid race between canceling CT and disposing it.
        if (_deadlineExceededCompleteTcs != null)
        {
            await _deadlineExceededCompleteTcs.Task;
        }
        DisposeCore();
    }

    private void DisposeCore()
    {
        // Remove request abort registration before disposing _deadlineCts.
        // Don't want an aborted request to attempt to cancel a disposed CTS.
        _requestAbortedRegistration.Dispose();

        _deadlineCts?.Dispose();
    }
}
