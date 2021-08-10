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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Internal.Http;
using Grpc.Shared;
using Microsoft.Extensions.Logging;
using Log = Grpc.Net.Client.Internal.Retry.RetryCallBaseLog;

#if NETSTANDARD2_0
using ValueTask = System.Threading.Tasks.Task;
#endif

namespace Grpc.Net.Client.Internal.Retry
{
    internal abstract partial class RetryCallBase<TRequest, TResponse> : IGrpcCall<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly TaskCompletionSource<IGrpcCall<TRequest, TResponse>> _commitedCallTcs;
        private RetryCallBaseClientStreamReader<TRequest, TResponse>? _retryBaseClientStreamReader;
        private RetryCallBaseClientStreamWriter<TRequest, TResponse>? _retryBaseClientStreamWriter;
        private CancellationTokenRegistration? _ctsRegistration;

        protected object Lock { get; } = new object();
        protected ILogger Logger { get; }
        protected Method<TRequest, TResponse> Method { get; }
        protected CallOptions Options { get; }
        protected int MaxRetryAttempts { get; }
        protected CancellationTokenSource CancellationTokenSource { get; }
        protected TaskCompletionSource<IGrpcCall<TRequest, TResponse>?>? NewActiveCallTcs { get; set; }
        protected bool Disposed { get; private set; }

        public GrpcChannel Channel { get; }
        public Task<IGrpcCall<TRequest, TResponse>> CommitedCallTask => _commitedCallTcs.Task;
        public IAsyncStreamReader<TResponse>? ClientStreamReader => _retryBaseClientStreamReader ??= new RetryCallBaseClientStreamReader<TRequest, TResponse>(this);
        public IClientStreamWriter<TRequest>? ClientStreamWriter => _retryBaseClientStreamWriter ??= new RetryCallBaseClientStreamWriter<TRequest, TResponse>(this);
        public WriteOptions? ClientStreamWriteOptions { get; internal set; }
        public bool ClientStreamComplete { get; set; }

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
            CancellationTokenSource.Token.Register(state => ((RetryCallBase<TRequest, TResponse>)state!).OnCancellation(), this);

            // If the passed in token is canceled then we want to cancel the retry cancellation token.
            // Note that if the token is already canceled then callback is run inline.
            if (options.CancellationToken.CanBeCanceled)
            {
                _ctsRegistration = options.CancellationToken.Register(state => ((RetryCallBase<TRequest, TResponse>)state!).CancellationTokenSource.Cancel(), this);
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
                Log.MaxAttemptsLimited(Logger, retryAttempts, Channel.MaxRetryAttempts.GetValueOrDefault());
                MaxRetryAttempts = Channel.MaxRetryAttempts.GetValueOrDefault();
            }
            else
            {
                MaxRetryAttempts = retryAttempts;
            }
        }

        public async Task<TResponse> GetResponseAsync()
        {
            var call = await CommitedCallTask.ConfigureAwait(false);
            return await call.GetResponseAsync().ConfigureAwait(false);
        }

        public async Task<Metadata> GetResponseHeadersAsync()
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

            ValueTask WriteAsync(TRequest request, Stream stream)
            {
                return WriteNewMessage(call, stream, call.Options, request);
            }
        }

        private PushStreamContent<TRequest, TResponse> CreatePushStreamContent(GrpcCall<TRequest, TResponse> call, HttpContentClientStreamWriter<TRequest, TResponse> clientStreamWriter)
        {
            return new PushStreamContent<TRequest, TResponse>(clientStreamWriter, async requestStream =>
            {
                ValueTask writeTask;
                lock (Lock)
                {
                    Log.SendingBufferedMessages(Logger, BufferedMessages.Count);

                    if (BufferedMessages.Count == 0)
                    {
#if NETSTANDARD2_0
                        writeTask = Task.CompletedTask;
#else
                        writeTask = default;
#endif
                    }
                    else if (BufferedMessages.Count == 1)
                    {
                        writeTask = call.WriteMessageAsync(requestStream, BufferedMessages[0], call.CancellationToken);
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

            static async ValueTask WriteBufferedMessages(GrpcCall<TRequest, TResponse> call, Stream requestStream, ReadOnlyMemory<byte>[] bufferedMessages)
            {
                foreach (var writtenMessage in bufferedMessages)
                {
                    await call.WriteMessageAsync(requestStream, writtenMessage, call.CancellationToken).ConfigureAwait(false);
                }
            }
        }

        protected abstract void StartCore(Action<GrpcCall<TRequest, TResponse>> startCallFunc);

        public abstract Task ClientStreamCompleteAsync();

        public abstract Task ClientStreamWriteAsync(TRequest message);

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

        protected async ValueTask WriteNewMessage(GrpcCall<TRequest, TResponse> call, Stream writeStream, CallOptions callOptions, TRequest message)
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
        }

        protected void CommitCall(IGrpcCall<TRequest, TResponse> call, CommitReason commitReason)
        {
            lock (Lock)
            {
                if (!CommitedCallTask.IsCompletedSuccessfully())
                {
                    // The buffer size is verified in unit tests after calls are completed.
                    // Clear the buffer before commiting call.
                    ClearRetryBuffer();

                    OnCommitCall(call);

                    // Log before committing for unit tests.
                    Log.CallCommited(Logger, commitReason);

                    NewActiveCallTcs?.SetResult(null);
                    _commitedCallTcs.SetResult(call);
                }
            }
        }

        protected abstract void OnCommitCall(IGrpcCall<TRequest, TResponse> call);

        protected bool HasClientStream()
        {
            return Method.Type == MethodType.ClientStreaming || Method.Type == MethodType.DuplexStreaming;
        }

        protected void SetNewActiveCallUnsynchronized(IGrpcCall<TRequest, TResponse> call)
        {
            Debug.Assert(!CommitedCallTask.IsCompletedSuccessfully());
            Debug.Assert(Monitor.IsEntered(Lock));

            if (NewActiveCallTcs != null)
            {
                // Run continuation synchronously so awaiters execute inside the lock
                NewActiveCallTcs.SetResult(call);
                NewActiveCallTcs = new TaskCompletionSource<IGrpcCall<TRequest, TResponse>?>(TaskCreationOptions.None);
            }
        }

        Task IGrpcCall<TRequest, TResponse>.WriteClientStreamAsync<TState>(Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, ValueTask> writeFunc, TState state)
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
                _ctsRegistration?.Dispose();
                CancellationTokenSource.Cancel();

                if (CommitedCallTask.IsCompletedSuccessfully())
                {
                    CommitedCallTask.Result.Dispose();
                }

                ClearRetryBuffer();
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
            return new StatusGrpcCall<TRequest, TResponse>(status);
        }

        protected void HandleUnexpectedError(Exception ex)
        {
            IGrpcCall<TRequest, TResponse> resolvedCall;
            CommitReason commitReason;

            // Cancellation token triggered by dispose could throw here.
            if (ex is OperationCanceledException && CancellationTokenSource.IsCancellationRequested)
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
                    resolvedCall = CreateStatusCall(Disposed ? GrpcProtocolConstants.DisposeCanceledStatus : GrpcProtocolConstants.ClientCanceledStatus);
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
    }
}
