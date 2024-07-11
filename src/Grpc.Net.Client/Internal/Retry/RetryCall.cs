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

internal sealed class RetryCall<TRequest, TResponse> : RetryCallBase<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    // Getting logger name from generic type is slow. Cached copy.
    private const string LoggerName = "Grpc.Net.Client.Internal.RetryCall";

    private readonly RetryPolicyInfo _retryPolicy;

    private int _nextRetryDelayMilliseconds;

    private GrpcCall<TRequest, TResponse>? _activeCall;

    public RetryCall(RetryPolicyInfo retryPolicy, GrpcChannel channel, Method<TRequest, TResponse> method, CallOptions options)
        : base(channel, method, options, LoggerName, retryPolicy.MaxAttempts)
    {
        _retryPolicy = retryPolicy;

        _nextRetryDelayMilliseconds = Convert.ToInt32(retryPolicy.InitialBackoff.TotalMilliseconds);

        Channel.RegisterActiveCall(this);
    }

    private int CalculateNextRetryDelay()
    {
        var nextMilliseconds = _nextRetryDelayMilliseconds * _retryPolicy.BackoffMultiplier;
        nextMilliseconds = Math.Min(nextMilliseconds, _retryPolicy.MaxBackoff.TotalMilliseconds);

        return Convert.ToInt32(nextMilliseconds);
    }

    private CommitReason? EvaluateRetry(Status status, int? retryPushbackMilliseconds)
    {
        if (IsDeadlineExceeded())
        {
            return CommitReason.DeadlineExceeded;
        }

        if (IsRetryThrottlingActive())
        {
            return CommitReason.Throttled;
        }

        if (AttemptCount >= MaxRetryAttempts)
        {
            return CommitReason.ExceededAttemptCount;
        }

        if (retryPushbackMilliseconds != null)
        {
            if (retryPushbackMilliseconds >= 0)
            {
                return null;
            }
            else
            {
                return CommitReason.PushbackStop;
            }
        }

        if (!_retryPolicy.RetryableStatusCodes.Contains(status.StatusCode))
        {
            return CommitReason.FatalStatusCode;
        }

        return null;
    }

    private async Task StartRetry(Action<GrpcCall<TRequest, TResponse>> startCallFunc)
    {
        Log.StartingRetryWorker(Logger);

        try
        {
            // This is the main retry loop. It will:
            // 1. Check the result of the active call was successful.
            // 2. If it was unsuccessful then evaluate if the call can be retried.
            // 3. If it can be retried then start a new active call and begin again.
            while (true)
            {
                GrpcCall<TRequest, TResponse> currentCall;
                lock (Lock)
                {
                    // Start new call.
                    OnStartingAttempt();

                    currentCall = _activeCall = HttpClientCallInvoker.CreateGrpcCall<TRequest, TResponse>(Channel, Method, Options, AttemptCount, forceAsyncHttpResponse: true, CallWrapper);
                    startCallFunc(currentCall);

                    SetNewActiveCallUnsynchronized(currentCall);

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
                    httpResponse = await currentCall.HttpResponseTask.ConfigureAwait(false);
                    responseStatus = GrpcCall.ValidateHeaders(httpResponse, out _);
                }
                catch (RpcException ex)
                {
                    // A "drop" result from the load balancer should immediately stop the call,
                    // including ignoring the retry policy.
                    var dropValue = ex.Trailers.GetValue(GrpcProtocolConstants.DropRequestTrailer);
                    if (dropValue != null && bool.TryParse(dropValue, out var isDrop) && isDrop)
                    {
                        CommitCall(currentCall, CommitReason.Drop);
                        return;
                    }

                    currentCall.ResolveException(GrpcCall<TRequest, TResponse>.ErrorStartingCallMessage, ex, out responseStatus, out _);
                }
                catch (Exception ex)
                {
                    currentCall.ResolveException(GrpcCall<TRequest, TResponse>.ErrorStartingCallMessage, ex, out responseStatus, out _);
                }

                CancellationTokenSource.Token.ThrowIfCancellationRequested();

                // Check to see the response returned from the server makes the call commited.
                // 1. Null status code indicates the headers were valid and a "Response-Headers" response
                //    was received from the server.
                // 2. An OK response status at this point means a streaming call completed without
                //    sending any messages to the client.
                //
                // https://github.com/grpc/proposal/blob/master/A6-client-retries.md#when-retries-are-valid
                if (responseStatus == null)
                {
                    // Headers were returned. We're commited.
                    CommitCall(currentCall, CommitReason.ResponseHeadersReceived);

                    // Force yield here to prevent continuation running with any locks.
                    responseStatus = await CompatibilityHelpers.AwaitWithYieldAsync(currentCall.CallTask).ConfigureAwait(false);
                    if (responseStatus.Value.StatusCode == StatusCode.OK)
                    {
                        RetryAttemptCallSuccess();
                    }

                    // Commited so exit retry loop.
                    return;
                }
                else if (IsSuccessfulStreamingCall(responseStatus.Value))
                {
                    // Headers were returned. We're commited.
                    CommitCall(currentCall, CommitReason.ResponseHeadersReceived);
                    RetryAttemptCallSuccess();

                    // Commited so exit retry loop.
                    return;
                }

                if (CommitedCallTask.IsCompletedSuccessfully())
                {
                    // Call has already been commited. This could happen if written messages exceed
                    // buffer limits, which causes the call to immediately become commited and to clear buffers.
                    return;
                }

                var status = responseStatus.Value;
                var retryPushbackMS = GetRetryPushback(httpResponse);

                // Failures only count towards retry throttling if they have a known, retriable status.
                // This stops non-transient statuses, e.g. INVALID_ARGUMENT, from triggering throttling.
                if (_retryPolicy.RetryableStatusCodes.Contains(status.StatusCode) ||
                    retryPushbackMS < 0)
                {
                    RetryAttemptCallFailure();
                }

                var result = EvaluateRetry(status, retryPushbackMS);
                Log.RetryEvaluated(Logger, status.StatusCode, AttemptCount, result == null);

                if (result == null)
                {
                    TimeSpan delayDuration;
                    if (retryPushbackMS != null)
                    {
                        delayDuration = TimeSpan.FromMilliseconds(retryPushbackMS.Value);
                        _nextRetryDelayMilliseconds = retryPushbackMS.Value;
                    }
                    else
                    {
                        delayDuration = TimeSpan.FromMilliseconds(Channel.GetRandomNumber(0, Convert.ToInt32(_nextRetryDelayMilliseconds)));
                    }

                    Log.StartingRetryDelay(Logger, delayDuration);
                    await Task.Delay(delayDuration, CancellationTokenSource.Token).ConfigureAwait(false);

                    _nextRetryDelayMilliseconds = CalculateNextRetryDelay();

                    // Check if dispose was called on call.
                    CancellationTokenSource.Token.ThrowIfCancellationRequested();

                    // Clean up the failed call.
                    currentCall.Dispose();
                }
                else
                {
                    // Handle the situation where the call failed with a non-deadline status, but retry
                    // didn't happen because of deadline exceeded.
                    IGrpcCall<TRequest, TResponse> resolvedCall = (IsDeadlineExceeded() && !(currentCall.CallTask.IsCompletedSuccessfully() && currentCall.CallTask.Result.StatusCode == StatusCode.DeadlineExceeded))
                        ? CreateStatusCall(GrpcProtocolConstants.DeadlineExceededStatus)
                        : currentCall;

                    // Can't retry.
                    // Signal public API exceptions that they should finish throwing and then exit the retry loop.
                    CommitCall(resolvedCall, result.Value);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            HandleUnexpectedError(ex);
        }
        finally
        {
            if (CommitedCallTask.IsCompletedSuccessfully())
            {
                if (CommitedCallTask.Result is GrpcCall<TRequest, TResponse> call)
                {
                    // Ensure response task is created before waiting to the end.
                    // Allows cancellation exceptions to be observed in cleanup.
                    if (!HasResponseStream())
                    {
                        _ = GetResponseAsync();
                    }

                    // Wait until the commited call is finished and then clean up retry call.
                    // Force yield here to prevent continuation running with any locks.
                    var status = await CompatibilityHelpers.AwaitWithYieldAsync(call.CallTask).ConfigureAwait(false);

                    var observeExceptions = status.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded;
                    Cleanup(observeExceptions);
                }
            }

            Log.StoppingRetryWorker(Logger);
        }
    }

    private bool IsSuccessfulStreamingCall(Status responseStatus)
    {
        if (responseStatus.StatusCode != StatusCode.OK)
        {
            return false;
        }

        return HasResponseStream();
    }

    protected override void OnCommitCall(IGrpcCall<TRequest, TResponse> call)
    {
        Debug.Assert(Monitor.IsEntered(Lock));

        _activeCall = null;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        // Don't dispose the active call inside the retry lock.
        // Canceling the call could cause callbacks to run on other threads that want to aquire this lock, causing an app deadlock.
        GrpcCall<TRequest, TResponse>? activeCall = null;
        lock (Lock)
        {
            activeCall = _activeCall;
        }
        activeCall?.Dispose();
    }

    protected override void StartCore(Action<GrpcCall<TRequest, TResponse>> startCallFunc)
    {
        _ = StartRetry(startCallFunc);
    }

    public override Task ClientStreamCompleteAsync()
    {
        ClientStreamComplete = true;

        return DoClientStreamActionAsync(async call =>
        {
            await call.ClientStreamWriter!.CompleteAsync().ConfigureAwait(false);
        });
    }

    public override async Task ClientStreamWriteAsync(TRequest message, CancellationToken cancellationToken)
    {
        using var registration = (cancellationToken.CanBeCanceled && cancellationToken != Options.CancellationToken)
            ? RegisterRetryCancellationToken(cancellationToken)
            : default;

        // The retry client stream writer prevents multiple threads from reaching here.
        await DoClientStreamActionAsync(async call =>
        {
            CompatibilityHelpers.Assert(call.ClientStreamWriter != null);

            if (ClientStreamWriteOptions != null)
            {
                call.ClientStreamWriter.WriteOptions = ClientStreamWriteOptions;
            }

            call.TryRegisterCancellation(cancellationToken, out var registration);
            try
            {
                await call.WriteClientStreamAsync(WriteNewMessage, message, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                registration?.Dispose();
            }

            lock (Lock)
            {
                BufferedCurrentMessage = false;
            }

            if (ClientStreamComplete)
            {
                await call.ClientStreamWriter.CompleteAsync().ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private async Task DoClientStreamActionAsync(Func<IGrpcCall<TRequest, TResponse>, Task> action)
    {
        // During a client streaming or bidirectional streaming call the app will call
        // WriteAsync and CompleteAsync on the call request stream. If the call fails then
        // an error will be thrown from those methods.
        //
        // The logic here will get the active call, apply the app action to the request stream.
        // If there is an error we wait for the new active call and then run the user action on it again.
        // Keep going until either the action succeeds, or there is no new active call
        // because of exceeded attempts, non-retry status code or retry throttling.

        var call = await GetActiveCallAsync(previousCall: null).ConfigureAwait(false);
        while (true)
        {
            try
            {
                await action(call!).ConfigureAwait(false);
                return;
            }
            catch
            {
                call = await GetActiveCallAsync(previousCall: call).ConfigureAwait(false);
                if (call == null)
                {
                    throw;
                }
            }
        }
    }

    private Task<IGrpcCall<TRequest, TResponse>?> GetActiveCallAsync(IGrpcCall<TRequest, TResponse>? previousCall)
    {
        Debug.Assert(NewActiveCallTcs != null);

        lock (Lock)
        {
            // Return currently active call if there is one, and its not the previous call.
            if (_activeCall != null && previousCall != _activeCall)
            {
                return Task.FromResult<IGrpcCall<TRequest, TResponse>?>(_activeCall);
            }

            // Wait to see whether new call will be made
            return GetActiveCallUnsynchronizedAsync(previousCall);
        }
    }
}
