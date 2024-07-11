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
using Grpc.Core;
using Grpc.Shared;
using Log = Grpc.Net.Client.Internal.Retry.RetryCallBaseLog;

namespace Grpc.Net.Client.Internal.Retry;

internal sealed partial class HedgingCall<TRequest, TResponse> : RetryCallBase<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    // Getting logger name from generic type is slow. Cached copy.
    private const string LoggerName = "Grpc.Net.Client.Internal.HedgingCall";

    private readonly HedgingPolicyInfo _hedgingPolicy;

    private CancellationTokenSource? _hedgingDelayCts;
    private TaskCompletionSource<object?>? _delayInterruptTcs;
    private TimeSpan? _pushbackDelay;

    private TaskCompletionSource<bool>? _writeClientMessageTcs;
    private int _writeClientMessageCount;

    // Internal for testing
    internal List<IGrpcCall<TRequest, TResponse>> _activeCalls { get; }
    internal Task? CreateHedgingCallsTask { get; set; }

    public HedgingCall(HedgingPolicyInfo hedgingPolicy, GrpcChannel channel, Method<TRequest, TResponse> method, CallOptions options)
        : base(channel, method, options, LoggerName, hedgingPolicy.MaxAttempts)
    {
        _hedgingPolicy = hedgingPolicy;
        _activeCalls = new List<IGrpcCall<TRequest, TResponse>>();

        if (_hedgingPolicy.HedgingDelay > TimeSpan.Zero)
        {
            _delayInterruptTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _hedgingDelayCts = new CancellationTokenSource();
        }

        Channel.RegisterActiveCall(this);
    }

    private async Task StartCall(Action<GrpcCall<TRequest, TResponse>> startCallFunc)
    {
        GrpcCall<TRequest, TResponse>? call = null;

        try
        {
            lock (Lock)
            {
                if (CommitedCallTask.IsCompletedSuccessfully())
                {
                    // Call has already been commited. This could happen if written messages exceed
                    // buffer limits, which causes the call to immediately become commited and to clear buffers.
                    return;
                }

                OnStartingAttempt();

                call = HttpClientCallInvoker.CreateGrpcCall<TRequest, TResponse>(Channel, Method, Options, AttemptCount, forceAsyncHttpResponse: true, CallWrapper);
                _activeCalls.Add(call);

                startCallFunc(call);

                SetNewActiveCallUnsynchronized(call);

                if (CommitedCallTask.IsCompletedSuccessfully())
                {
                    // Call has already been commited. This could happen if written messages exceed
                    // buffer limits, which causes the call to immediately become commited and to clear buffers.
                    return;
                }
            }

            Status? responseStatus;

            HttpResponseMessage? httpResponse = null;
            try
            {
                httpResponse = await call.HttpResponseTask.ConfigureAwait(false);
                responseStatus = GrpcCall.ValidateHeaders(httpResponse, out _);
            }
            catch (RpcException ex)
            {
                // A "drop" result from the load balancer should immediately stop the call,
                // including ignoring the retry policy.
                var dropValue = ex.Trailers.GetValue(GrpcProtocolConstants.DropRequestTrailer);
                if (dropValue != null && bool.TryParse(dropValue, out var isDrop) && isDrop)
                {
                    CommitCall(call, CommitReason.Drop);
                    return;
                }

                call.ResolveException(GrpcCall<TRequest, TResponse>.ErrorStartingCallMessage, ex, out responseStatus, out _);
            }
            catch (Exception ex)
            {
                call.ResolveException(GrpcCall<TRequest, TResponse>.ErrorStartingCallMessage, ex, out responseStatus, out _);
            }

            CancellationTokenSource.Token.ThrowIfCancellationRequested();

            // Check to see the response returned from the server makes the call commited
            // Null status code indicates the headers were valid and a "Response-Headers" response
            // was received from the server.
            // https://github.com/grpc/proposal/blob/master/A6-client-retries.md#when-retries-are-valid
            if (responseStatus == null)
            {
                // Headers were returned. We're commited.
                CommitCall(call, CommitReason.ResponseHeadersReceived);

                // Wait until the call has finished and then check its status code
                // to update retry throttling tokens.
                // Force yield here to prevent continuation running with any locks.
                var status = await CompatibilityHelpers.AwaitWithYieldAsync(call.CallTask).ConfigureAwait(false);
                if (status.StatusCode == StatusCode.OK)
                {
                    RetryAttemptCallSuccess();
                }
                return;
            }

            lock (Lock)
            {
                var status = responseStatus.Value;
                if (IsDeadlineExceeded())
                {
                    // Deadline has been exceeded so immediately commit call.
                    CommitCall(call, CommitReason.DeadlineExceeded);
                }
                else if (!_hedgingPolicy.NonFatalStatusCodes.Contains(status.StatusCode))
                {
                    CommitCall(call, CommitReason.FatalStatusCode);
                }
                else if (_activeCalls.Count == 1 && AttemptCount >= MaxRetryAttempts)
                {
                    // This is the last active call and no more will be made.
                    CommitCall(call, CommitReason.ExceededAttemptCount);
                }
                else
                {
                    // Call failed but it didn't exceed deadline, have a fatal status code
                    // and there are remaining attempts available. Is a chance it will be retried.
                    //
                    // Increment call failure out. Needs to happen before checking throttling.
                    RetryAttemptCallFailure();

                    if (_activeCalls.Count == 1 && IsRetryThrottlingActive())
                    {
                        // This is the last active call and throttling is active.
                        CommitCall(call, CommitReason.Throttled);
                    }
                    else
                    {
                        var retryPushbackMS = GetRetryPushback(httpResponse);

                        // No need to interrupt if we started with no delay and all calls
                        // have already been made when hedging starting.
                        if (_delayInterruptTcs != null)
                        {
                            if (retryPushbackMS >= 0)
                            {
                                _pushbackDelay = TimeSpan.FromMilliseconds(retryPushbackMS.Value);
                            }
                            _delayInterruptTcs.TrySetResult(null);
                        }

                        // Call isn't used and can be cancelled.
                        // Note that the call could have already been removed and disposed if the
                        // hedging call has been finalized or disposed.
                        if (_activeCalls.Remove(call))
                        {
                            call.Dispose();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HandleUnexpectedError(ex);
        }
        finally
        {
            if (CommitedCallTask.IsCompletedSuccessfully() && CommitedCallTask.Result == call)
            {
                // Ensure response task is created before waiting to the end.
                // Allows cancellation exceptions to be observed in cleanup.
                if (!HasResponseStream())
                {
                    _ = GetResponseAsync();
                }

                // Wait until the commited call is finished and then clean up hedging call.
                // Force yield here to prevent continuation running with any locks.
                var status = await CompatibilityHelpers.AwaitWithYieldAsync(call.CallTask).ConfigureAwait(false);

                var observeExceptions = status.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded;
                Cleanup(observeExceptions);
            }
        }
    }

    protected override void OnCommitCall(IGrpcCall<TRequest, TResponse> call)
    {
        Debug.Assert(Monitor.IsEntered(Lock));

        _activeCalls.Remove(call);

        CleanUpUnsynchronized();
    }

    private void CleanUpUnsynchronized()
    {
        Debug.Assert(Monitor.IsEntered(Lock));

        while (_activeCalls.Count > 0)
        {
            _activeCalls[_activeCalls.Count - 1].Dispose();
            _activeCalls.RemoveAt(_activeCalls.Count - 1);
        }
    }

    protected override void StartCore(Action<GrpcCall<TRequest, TResponse>> startCallFunc)
    {
        var hedgingDelay = _hedgingPolicy.HedgingDelay;
        if (hedgingDelay == TimeSpan.Zero)
        {
            // If there is no delay then start all call immediately
            while (AttemptCount < MaxRetryAttempts)
            {
                _ = StartCall(startCallFunc);

                // Don't send additional calls if retry throttling is active.
                if (IsRetryThrottlingActive())
                {
                    Log.AdditionalCallsBlockedByRetryThrottling(Logger);
                    break;
                }

                lock (Lock)
                {
                    // Don't send additional calls if call has been commited.
                    if (CommitedCallTask.IsCompletedSuccessfully())
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            CreateHedgingCallsTask = CreateHedgingCalls(startCallFunc);
        }
    }

    private async Task CreateHedgingCalls(Action<GrpcCall<TRequest, TResponse>> startCallFunc)
    {
        Log.StartingRetryWorker(Logger);

        try
        {
            var hedgingDelay = _hedgingPolicy.HedgingDelay;

            while (AttemptCount < MaxRetryAttempts)
            {
                _ = StartCall(startCallFunc);

                await HedgingDelayAsync(hedgingDelay).ConfigureAwait(false);

                if (IsDeadlineExceeded())
                {
                    CommitCall(CreateStatusCall(new Status(StatusCode.DeadlineExceeded, string.Empty)), CommitReason.DeadlineExceeded);
                    break;
                }
                else
                {
                    lock (Lock)
                    {
                        if (IsRetryThrottlingActive())
                        {
                            if (_activeCalls.Count == 0)
                            {
                                CommitCall(CreateStatusCall(GrpcProtocolConstants.ThrottledStatus), CommitReason.Throttled);
                            }
                            else
                            {
                                Log.AdditionalCallsBlockedByRetryThrottling(Logger);
                            }
                            break;
                        }

                        // Don't send additional calls if call has been commited.
                        if (CommitedCallTask.IsCompletedSuccessfully())
                        {
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            HandleUnexpectedError(ex);
        }
        finally
        {
            Log.StoppingRetryWorker(Logger);
        }
    }

    private async Task HedgingDelayAsync(TimeSpan hedgingDelay)
    {
        CompatibilityHelpers.Assert(_hedgingDelayCts != null);
        CompatibilityHelpers.Assert(_delayInterruptTcs != null);

        while (true)
        {
            CompatibilityHelpers.Assert(_hedgingDelayCts != null);

            var completedTask = await Task.WhenAny(Task.Delay(hedgingDelay, _hedgingDelayCts.Token), _delayInterruptTcs.Task).ConfigureAwait(false);
            if (completedTask != _delayInterruptTcs.Task)
            {
                // Task.Delay won. Check CTS to see if it won because of cancellation.
                _hedgingDelayCts.Token.ThrowIfCancellationRequested();
                return;
            }
            else
            {
                // Cancel the Task.Delay that's no longer needed.
                // https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/519ef7d231c01116f02bc04354816a735f2a36b6/AsyncGuidance.md#using-a-timeout
                _hedgingDelayCts.Cancel();
            }

            lock (Lock)
            {
                // If we reaching this point then the delay was interrupted.
                // Need to recreate the delay TCS/CTS for the next cycle.
                _delayInterruptTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _hedgingDelayCts = new CancellationTokenSource();

                // Interrupt could come from a pushback, or a failing call with a non-fatal status.
                if (_pushbackDelay != null)
                {
                    // Use pushback value and delay again
                    hedgingDelay = _pushbackDelay.Value;

                    _pushbackDelay = null;
                }
                else
                {
                    // Immediately return for non-fatal status.
                    return;
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        lock (Lock)
        {
            CleanUpUnsynchronized();
        }
    }

    public override Task ClientStreamCompleteAsync()
    {
        ClientStreamComplete = true;

        return DoClientStreamActionAsync(calls =>
        {
            var completeTasks = new Task[calls.Count];
            for (var i = 0; i < calls.Count; i++)
            {
                completeTasks[i] = calls[i].ClientStreamWriter!.CompleteAsync();
            }

            return Task.WhenAll(completeTasks);
        });
    }

    public override async Task ClientStreamWriteAsync(TRequest message, CancellationToken cancellationToken)
    {
        // The retry client stream writer prevents multiple threads from reaching here.
        
        // Hedging allows:
        // 1. Multiple calls to happen simultaniously
        // 2. Starting a new call once existing calls have failed.
        // 
        // We don't want to exit this method until either the message has been sent to the
        // server at least once successfully, or the call fails.
        // If there is an error sending the message then wait for another call to successfully
        // send the buffered data.
        //
        // This is done by awaiting a TCS until either:
        // 1. The message count is sent.
        // 2. The call is commited with an error, throwing an exception.
        _writeClientMessageCount++;
        _writeClientMessageTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Register after TCS is created so immediate failure is propagated to TCS.
        using var registration = (cancellationToken.CanBeCanceled && cancellationToken != Options.CancellationToken)
            ? RegisterRetryCancellationToken(cancellationToken)
            : default;

        try
        {
            await DoClientStreamActionAsync(async calls =>
            {
                // Note: There may be less write tasks than calls passed in.
                // For example, a large message could cause the call to be commited and all active calls are removed.
                var writeTasks = new List<Task>(calls.Count);
                List<CancellationTokenRegistration>? registrations = null;

                for (var i = 0; i < calls.Count; i++)
                {
                    var c = calls[i];
                    if (c.TryRegisterCancellation(cancellationToken, out var registration))
                    {
                        registrations ??= new List<CancellationTokenRegistration>(calls.Count);
                        registrations.Add(registration.Value);
                    }

                    var writeTask = c.WriteClientStreamAsync(WriteNewMessage, message, cancellationToken);

                    writeTasks.Add(writeTask);
                }

                try
                {
                    await Task.WhenAll(writeTasks).ConfigureAwait(false);
                }
                finally
                {
                    if (registrations != null)
                    {
                        foreach (var registration in registrations)
                        {
                            registration.Dispose();
                        }
                    }
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            lock (Lock)
            {
                if (CommitedCallTask.IsCompletedSuccessfully())
                {
                    throw;
                }
            }

            // Flag indicates whether buffered message was successfully written.
            var success = await _writeClientMessageTcs.Task.ConfigureAwait(false);
            if (success)
            {
                return;
            }
            else
            {
                var commitedCall = CommitedCallTask.Result;
                throw commitedCall.CreateFailureStatusException(commitedCall.GetStatus());
            }
        }
        finally
        {
            _writeClientMessageTcs = null;
        }

        lock (Lock)
        {
            BufferedCurrentMessage = false;
        }
    }

    private Task DoClientStreamActionAsync(Func<IList<IGrpcCall<TRequest, TResponse>>, Task> action)
    {
        // During a client streaming or bidirectional streaming call the app will call
        // WriteAsync and CompleteAsync on the call request stream. If the call fails then
        // an error will be thrown from those methods.
        //
        // The logic here will get the active call, apply the app action to the request stream.
        // If there is an error we wait for the new active call and then run the user action on it again.
        // Keep going until either the action succeeds, or there is no new active call
        // because of exceeded attempts, non-retry status code or retry throttling.
        //
        // Because of hedging, multiple active calls can be in-progress. Apply action to all.

        lock (Lock)
        {
            return _activeCalls.Count > 0
                ? action(_activeCalls)
                : WaitForCallUnsynchronizedAsync(action);
        }

        async Task WaitForCallUnsynchronizedAsync(Func<IList<IGrpcCall<TRequest, TResponse>>, Task> action)
        {
            var call = await GetActiveCallUnsynchronizedAsync(previousCall: null).ConfigureAwait(false);
            await action(new[] { call! }).ConfigureAwait(false);
        }
    }

    protected override void OnBufferMessageWritten(int count)
    {
        if (count >= _writeClientMessageCount)
        {
            _writeClientMessageTcs?.TrySetResult(true);
        }
    }

    protected override void OnCancellation()
    {
        _hedgingDelayCts?.Cancel();
        _writeClientMessageTcs?.TrySetResult(false);
    }
}
