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

using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Grpc.Net.Client.Internal.Http;
using Grpc.Shared;
using Microsoft.Extensions.Logging;
using Log = Grpc.Net.Client.Internal.Retry.RetryCallBaseLog;

namespace Grpc.Net.Client.Internal.Retry;

internal abstract partial class RetryCallBase<TRequest, TResponse> : IGrpcCall<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly TaskCompletionSource<IGrpcCall<TRequest, TResponse>> _commitedCallTcs;
    private RetryCallBaseClientStreamReader<TRequest, TResponse>? _retryBaseClientStreamReader;
    private RetryCallBaseClientStreamWriter<TRequest, TResponse>? _retryBaseClientStreamWriter;
    private Task<TResponse>? _responseTask;
    private Task<Metadata>? _responseHeadersTask;
    private TRequest? _request;
    private bool _commitStarted;

    // Internal for unit testing.
    internal CancellationTokenRegistration? _ctsRegistration;

    protected object Lock { get; } = new object();
    protected ILogger Logger { get; }
    protected Method<TRequest, TResponse> Method { get; }
    protected CallOptions Options { get; }
    protected int MaxRetryAttempts { get; }
    protected CancellationTokenSource CancellationTokenSource { get; }
    protected TaskCompletionSource<IGrpcCall<TRequest, TResponse>?>? NewActiveCallTcs { get; set; }

    public GrpcChannel Channel { get; }
    public Task<IGrpcCall<TRequest, TResponse>> CommitedCallTask => _commitedCallTcs.Task;
    public IAsyncStreamReader<TResponse>? ClientStreamReader => _retryBaseClientStreamReader ??= new RetryCallBaseClientStreamReader<TRequest, TResponse>(this);
    public IClientStreamWriter<TRequest>? ClientStreamWriter => _retryBaseClientStreamWriter ??= new RetryCallBaseClientStreamWriter<TRequest, TResponse>(this);
    public WriteOptions? ClientStreamWriteOptions { get; internal set; }
    public bool ClientStreamComplete { get; set; }
    public int MessagesWritten { get; private set; }
    public bool Disposed { get; private set; }
    public object? CallWrapper { get; set; }
    public bool ResponseFinished => CommitedCallTask.IsCompletedSuccessfully() ? CommitedCallTask.Result.ResponseFinished : false;
    public int MessagesRead => CommitedCallTask.IsCompletedSuccessfully() ? CommitedCallTask.Result.MessagesRead : 0;

    protected int AttemptCount { get; private set; }
    protected List<ReadOnlyMemory<byte>> BufferedMessages { get; }
    protected long CurrentCallBufferSize { get; set; }
    protected bool BufferedCurrentMessage { get; set; }

    protected RetryCallBase(GrpcChannel channel, Method<TRequest, TResponse> method, CallOptions options, string loggerName, int retryAttempts)
    {
        Logger = channel.LoggerFactory.CreateLogger(loggerName);
        Channel = channel;
        Method = method;
        Options = options;
        _commitedCallTcs = new TaskCompletionSource<IGrpcCall<TRequest, TResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        BufferedMessages = new List<ReadOnlyMemory<byte>>();

        // Raise OnCancellation event for cancellation related clean up.
        CancellationTokenSource = new CancellationTokenSource();
        CancellationTokenSource.Token.Register(static state => ((RetryCallBase<TRequest, TResponse>)state!).OnCancellation(), this);

        // If the passed in token is canceled then we want to cancel the retry cancellation token.
        // Note that if the token is already canceled then callback is run inline.
        if (options.CancellationToken.CanBeCanceled)
        {
            _ctsRegistration = RegisterRetryCancellationToken(options.CancellationToken);
        }

        var deadline = Options.Deadline.GetValueOrDefault(DateTime.MaxValue);
        if (deadline != DateTime.MaxValue)
        {
            var timeout = CommonGrpcProtocolHelpers.GetTimerDueTime(deadline - Channel.Clock.UtcNow, Channel.MaxTimerDueTime);
            CancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(timeout));
        }

        if (HasClientStream())
        {
            // Run continuation synchronously so awaiters execute inside the lock
            NewActiveCallTcs = new TaskCompletionSource<IGrpcCall<TRequest, TResponse>?>(TaskCreationOptions.None);
        }

        if (retryAttempts > Channel.MaxRetryAttempts)
        {
            Log.MaxAttemptsLimited(Logger, retryAttempts, Channel.MaxRetryAttempts.Value);
            MaxRetryAttempts = Channel.MaxRetryAttempts.Value;
        }
        else
        {
            MaxRetryAttempts = retryAttempts;
        }
    }

    public Task<TResponse> GetResponseAsync() => _responseTask ??= GetResponseCoreAsync();

    private async Task<TResponse> GetResponseCoreAsync()
    {
        var call = await CommitedCallTask.ConfigureAwait(false);
        return await call.GetResponseAsync().ConfigureAwait(false);
    }

    public Task<Metadata> GetResponseHeadersAsync()
    {
        if (_responseHeadersTask == null)
        {
            _responseHeadersTask = GetResponseHeadersCoreAsync();

            // ResponseHeadersAsync could be called inside a client interceptor when a call is wrapped.
            // Most people won't use the headers result. Observed exception to avoid unobserved exception event.
            _responseHeadersTask.ObserveException();

            // If there was an error fetching response headers then it's likely the same error is reported
            // by response TCS. The user is unlikely to observe both errors.
            // Observed exception to avoid unobserved exception event.
            _responseTask?.ObserveException();
        }

        return _responseHeadersTask;
    }

    private async Task<Metadata> GetResponseHeadersCoreAsync()
    {
        var call = await CommitedCallTask.ConfigureAwait(false);
        return await call.GetResponseHeadersAsync().ConfigureAwait(false);
    }

    public Status GetStatus()
    {
        if (CommitedCallTask.IsCompletedSuccessfully())
        {
            return CommitedCallTask.Result.GetStatus();
        }

        throw new InvalidOperationException("Unable to get the status because the call is not complete.");
    }

    public Metadata GetTrailers()
    {
        if (CommitedCallTask.IsCompletedSuccessfully())
        {
            return CommitedCallTask.Result.GetTrailers();
        }

        throw new InvalidOperationException("Can't get the call trailers because the call has not completed successfully.");
    }

    public void Dispose() => Dispose(true);

    public void StartUnary(TRequest request)
    {
        _request = request;
        StartCore(call => call.StartUnaryCore(CreatePushUnaryContent(request, call)));
    }

    public void StartClientStreaming()
    {
        StartCore(call =>
        {
            var clientStreamWriter = new HttpContentClientStreamWriter<TRequest, TResponse>(call);
            var content = CreatePushStreamContent(call, clientStreamWriter);
            call.StartClientStreamingCore(clientStreamWriter, content);
        });
    }

    public void StartServerStreaming(TRequest request)
    {
        StartCore(call => call.StartServerStreamingCore(CreatePushUnaryContent(request, call)));
    }

    public void StartDuplexStreaming()
    {
        StartCore(call =>
        {
            var clientStreamWriter = new HttpContentClientStreamWriter<TRequest, TResponse>(call);
            var content = CreatePushStreamContent(call, clientStreamWriter);
            call.StartDuplexStreamingCore(clientStreamWriter, content);
        });
    }

    private HttpContent CreatePushUnaryContent(TRequest request, GrpcCall<TRequest, TResponse> call)
    {
        return Channel.HttpHandlerType != HttpHandlerType.WinHttpHandler
            ? new PushUnaryContent<TRequest, TResponse>(request, WriteAsync)
            : new WinHttpUnaryContent<TRequest, TResponse>(request, WriteAsync, call);

        Task WriteAsync(TRequest request, Stream stream)
        {
            return WriteNewMessage(call, stream, call.Options, request);
        }
    }

    private PushStreamContent<TRequest, TResponse> CreatePushStreamContent(GrpcCall<TRequest, TResponse> call, HttpContentClientStreamWriter<TRequest, TResponse> clientStreamWriter)
    {
        return new PushStreamContent<TRequest, TResponse>(clientStreamWriter, async requestStream =>
        {
            Task writeTask;
            lock (Lock)
            {
                Log.SendingBufferedMessages(Logger, BufferedMessages.Count);

                if (BufferedMessages.Count == 0)
                {
                    writeTask = Task.CompletedTask;
                }
                else
                {
                    // Copy messages to a new collection in lock for thread-safety.
                    var bufferedMessageCopy = BufferedMessages.ToArray();
                    writeTask = WriteBufferedMessages(call, requestStream, bufferedMessageCopy);
                }
            }

            await writeTask.ConfigureAwait(false);

            if (ClientStreamComplete)
            {
                await call.ClientStreamWriter!.CompleteAsync().ConfigureAwait(false);
            }
        });
    }

    private async Task WriteBufferedMessages(GrpcCall<TRequest, TResponse> call, Stream requestStream, ReadOnlyMemory<byte>[] bufferedMessages)
    {
        for (var i = 0; i < bufferedMessages.Length; i++)
        {
            await call.WriteMessageAsync(requestStream, bufferedMessages[i], call.CancellationToken).ConfigureAwait(false);

            // Flush stream to ensure messages are sent immediately.
            await requestStream.FlushAsync(call.CancellationToken).ConfigureAwait(false);

            OnBufferMessageWritten(i + 1);
        }
    }

    protected virtual void OnBufferMessageWritten(int count)
    {
    }

    protected abstract void StartCore(Action<GrpcCall<TRequest, TResponse>> startCallFunc);

    public abstract Task ClientStreamCompleteAsync();

    public abstract Task ClientStreamWriteAsync(TRequest message, CancellationToken cancellationToken);

    protected CancellationTokenRegistration RegisterRetryCancellationToken(CancellationToken cancellationToken)
    {
        return cancellationToken.Register(
            static state =>
            {
                var call = (RetryCallBase<TRequest, TResponse>)state!;

                Log.CanceledRetry(call.Logger);
                call.CancellationTokenSource.Cancel();
            },
            this);
    }

    protected bool IsDeadlineExceeded()
    {
        return Options.Deadline != null && Options.Deadline <= Channel.Clock.UtcNow;
    }

    protected int? GetRetryPushback(HttpResponseMessage? httpResponse)
    {
        // https://github.com/grpc/proposal/blob/master/A6-client-retries.md#pushback
        if (httpResponse != null)
        {
            var headerValue = GrpcProtocolHelpers.GetHeaderValue(httpResponse.Headers, GrpcProtocolConstants.RetryPushbackHeader);
            if (headerValue != null)
            {
                Log.RetryPushbackReceived(Logger, headerValue);

                // A non-integer value means the server wants retries to stop.
                // Resolve non-integer value to a negative integer which also means stop.
                return int.TryParse(headerValue, out var value) ? value : -1;
            }
        }

        return null;
    }

    protected byte[] SerializePayload(GrpcCall<TRequest, TResponse> call, CallOptions callOptions, TRequest request)
    {
        var serializationContext = call.SerializationContext;
        serializationContext.CallOptions = callOptions;
        serializationContext.Initialize();

        try
        {
            call.Method.RequestMarshaller.ContextualSerializer(request, serializationContext);

            // Need to take a copy because the serialization context will returned a rented buffer.
            return serializationContext.GetWrittenPayload().ToArray();
        }
        finally
        {
            serializationContext.Reset();
        }
    }

    protected async Task WriteNewMessage(GrpcCall<TRequest, TResponse> call, Stream writeStream, CallOptions callOptions, TRequest message)
    {
        // Serialize current message and add to the buffer.
        ReadOnlyMemory<byte> messageData;

        lock (Lock)
        {
            if (!BufferedCurrentMessage)
            {
                messageData = SerializePayload(call, callOptions, message);

                // Don't buffer message data if the call has been commited.
                if (!CommitedCallTask.IsCompletedSuccessfully())
                {
                    if (!TryAddToRetryBuffer(messageData))
                    {
                        CommitCall(call, CommitReason.BufferExceeded);
                    }
                    else
                    {
                        BufferedCurrentMessage = true;

                        Log.MessageAddedToBuffer(Logger, messageData.Length, CurrentCallBufferSize);
                    }
                }
            }
            else
            {
                // There is a race between:
                // 1. A client stream starting for a new call. It will write all buffered messages, and
                // 2. Writing a new message here. The message may already have been buffered when the client
                //    stream started so we don't want to write it again.
                //
                // Check the client stream write count against the buffer message count to ensure all buffered
                // messages haven't already been written.
                if (call.MessagesWritten == BufferedMessages.Count)
                {
                    return;
                }

                messageData = BufferedMessages[BufferedMessages.Count - 1];
            }
        }

        await call.WriteMessageAsync(writeStream, messageData, callOptions.CancellationToken).ConfigureAwait(false);
        MessagesWritten++;
    }

    protected void CommitCall(IGrpcCall<TRequest, TResponse> call, CommitReason commitReason)
    {
        lock (Lock)
        {
            if (!_commitStarted)
            {
                // Specify that call is commiting. This is to prevent any chance of re-entrancy from logic run in OnCommitCall.
                _commitStarted = true;

                // The buffer size is verified in unit tests after calls are completed.
                // Clear the buffer before commiting call.
                ClearRetryBuffer();

                OnCommitCall(call);

                // Log before committing for unit tests.
                Log.CallCommited(Logger, commitReason);

                NewActiveCallTcs?.SetResult(null);
                _commitedCallTcs.SetResult(call);

                // If the commited call has finished and cleaned up then it is safe for
                // the wrapping retry call to clean up. This is required to unregister
                // from the cancellation token and avoid a memory leak.
                //
                // A commited call that has already cleaned up is likely a StatusGrpcCall.
                if (call.Disposed)
                {
                    Cleanup(observeExceptions: false);
                }
            }
        }
    }

    protected abstract void OnCommitCall(IGrpcCall<TRequest, TResponse> call);

    protected bool HasClientStream()
    {
        return Method.Type == MethodType.ClientStreaming || Method.Type == MethodType.DuplexStreaming;
    }

    protected bool HasResponseStream()
    {
        return Method.Type == MethodType.ServerStreaming || Method.Type == MethodType.DuplexStreaming;
    }

    protected void SetNewActiveCallUnsynchronized(IGrpcCall<TRequest, TResponse> call)
    {
        Debug.Assert(Monitor.IsEntered(Lock), "Should be called with lock.");

        if (NewActiveCallTcs != null)
        {
            // Run continuation synchronously so awaiters execute inside the lock
            NewActiveCallTcs.SetResult(call);
            NewActiveCallTcs = new TaskCompletionSource<IGrpcCall<TRequest, TResponse>?>(TaskCreationOptions.None);
        }
    }

    Task IGrpcCall<TRequest, TResponse>.WriteClientStreamAsync<TState>(Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, Task> writeFunc, TState state, CancellationToken cancellationTokens)
    {
        throw new NotSupportedException();
    }

    protected async Task<IGrpcCall<TRequest, TResponse>?> GetActiveCallUnsynchronizedAsync(IGrpcCall<TRequest, TResponse>? previousCall)
    {
        CompatibilityHelpers.Assert(NewActiveCallTcs != null);

        var call = await NewActiveCallTcs.Task.ConfigureAwait(false);

        Debug.Assert(Monitor.IsEntered(Lock));
        if (call == null)
        {
            call = await CommitedCallTask.ConfigureAwait(false);
        }

        // Avoid infinite loop.
        if (call == previousCall)
        {
            return null;
        }

        return call;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed)
        {
            return;
        }

        Disposed = true;

        if (disposing)
        {
            if (CommitedCallTask.IsCompletedSuccessfully())
            {
                CommitedCallTask.Result.Dispose();
            }

            Cleanup(observeExceptions: true);
        }
    }

    protected void Cleanup(bool observeExceptions)
    {
        Channel.FinishActiveCall(this);

        _ctsRegistration?.Dispose();
        _ctsRegistration = null;
        CancellationTokenSource.Cancel();

        ClearRetryBuffer();

        if (observeExceptions)
        {
            _responseTask?.ObserveException();
            _responseHeadersTask?.ObserveException();
        }
    }

    internal bool TryAddToRetryBuffer(ReadOnlyMemory<byte> message)
    {
        lock (Lock)
        {
            var messageSize = message.Length;
            if (CurrentCallBufferSize + messageSize > Channel.MaxRetryBufferPerCallSize)
            {
                return false;
            }
            if (!Channel.TryAddToRetryBuffer(messageSize))
            {
                return false;
            }

            CurrentCallBufferSize += messageSize;
            BufferedMessages.Add(message);
            return true;
        }
    }

    internal void ClearRetryBuffer()
    {
        lock (Lock)
        {
            if (BufferedMessages.Count > 0)
            {
                BufferedMessages.Clear();
                Channel.RemoveFromRetryBuffer(CurrentCallBufferSize);
                CurrentCallBufferSize = 0;
            }
        }
    }

    protected StatusGrpcCall<TRequest, TResponse> CreateStatusCall(Status status)
    {
        var call = new StatusGrpcCall<TRequest, TResponse>(status, Channel, Method, MessagesRead, _request);
        call.CallWrapper = CallWrapper;
        return call;
    }

    protected void HandleUnexpectedError(Exception ex)
    {
        IGrpcCall<TRequest, TResponse> resolvedCall;
        CommitReason commitReason;

        // Cancellation token triggered by dispose could throw here.
        if (ex is OperationCanceledException operationCanceledException && CancellationTokenSource.IsCancellationRequested)
        {
            // Cancellation could have been caused by an exceeded deadline.
            if (IsDeadlineExceeded())
            {
                commitReason = CommitReason.DeadlineExceeded;
                // An exceeded deadline inbetween calls means there is no active call.
                // Create a fake call that returns exceeded deadline status to the app.
                resolvedCall = CreateStatusCall(GrpcProtocolConstants.DeadlineExceededStatus);
            }
            else
            {
                commitReason = CommitReason.Canceled;
                Status status;
                if (Disposed)
                {
                    status = GrpcProtocolConstants.CreateDisposeCanceledStatus(exception: null);
                }
                else
                {
                    // Replace the OCE from CancellationTokenSource with an OCE that has the passed in cancellation token if it is canceled.
                    if (Options.CancellationToken.IsCancellationRequested && Options.CancellationToken != operationCanceledException.CancellationToken)
                    {
                        ex = new OperationCanceledException(Options.CancellationToken);
                    }
                    status = GrpcProtocolConstants.CreateClientCanceledStatus(ex);
                }
                resolvedCall = CreateStatusCall(status);
            }
        }
        else
        {
            commitReason = CommitReason.UnexpectedError;
            resolvedCall = CreateStatusCall(GrpcProtocolHelpers.CreateStatusFromException("Unexpected error during retry.", ex));

            // Only log unexpected errors.
            Log.ErrorRetryingCall(Logger, ex);
        }

        CommitCall(resolvedCall, commitReason);
    }

    protected void OnStartingAttempt()
    {
        Debug.Assert(Monitor.IsEntered(Lock));

        AttemptCount++;
        Log.StartingAttempt(Logger, AttemptCount);
    }

    protected virtual void OnCancellation()
    {
    }

    protected bool IsRetryThrottlingActive()
    {
        return Channel.RetryThrottling?.IsRetryThrottlingActive() ?? false;
    }

    protected void RetryAttemptCallSuccess()
    {
        Channel.RetryThrottling?.CallSuccess();
    }

    protected void RetryAttemptCallFailure()
    {
        Channel.RetryThrottling?.CallFailure();
    }

    public bool TryRegisterCancellation(CancellationToken cancellationToken, [NotNullWhen(true)] out CancellationTokenRegistration? cancellationTokenRegistration)
    {
        throw new NotSupportedException();
    }

    public Exception CreateFailureStatusException(Status status)
    {
        throw new NotSupportedException();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => GrpcProtocolConstants.GetDebugEnumerator(Channel, Method, _request);
}
