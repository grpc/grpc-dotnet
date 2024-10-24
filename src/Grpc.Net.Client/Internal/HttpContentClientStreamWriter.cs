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
using System.Runtime.ExceptionServices;
using Grpc.Core;
using Grpc.Shared;
using Log = Grpc.Net.Client.Internal.ClientStreamWriterBaseLog;

namespace Grpc.Net.Client.Internal;

[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(HttpContentClientStreamWriter<,>.HttpContentClientStreamWriterDebugView))]
internal sealed class HttpContentClientStreamWriter<TRequest, TResponse> : ClientStreamWriterBase<TRequest>
    where TRequest : class
    where TResponse : class
{
    // Getting logger name from generic type is slow
    private const string LoggerName = "Grpc.Net.Client.Internal.HttpContentClientStreamWriter";

    private readonly GrpcCall<TRequest, TResponse> _call;
    private bool _completeCalled;

    public TaskCompletionSource<Stream> WriteStreamTcs { get; }
    public TaskCompletionSource<bool> CompleteTcs { get; }

    public HttpContentClientStreamWriter(GrpcCall<TRequest, TResponse> call)
        : base(call.Channel.LoggerFactory.CreateLogger(LoggerName))
    {
        _call = call;

        // CompleteTcs doesn't use RunContinuationsAsynchronously because we want the caller of CompleteAsync
        // to wait until the TCS's awaiter, PushStreamContent, finishes completing the request.
        // This is required to avoid a race condition between the HttpContent completing, and sending an
        // END_STREAM flag to the server, and app code disposing the call, which will trigger a RST_STREAM
        // if HttpContent has finished.
        // See https://github.com/grpc/grpc-dotnet/issues/1394 for an example.
        CompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.None);

        WriteStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        WriteOptions = _call.Options.WriteOptions;
    }

    public override WriteOptions? WriteOptions { get; set; }

    public override Task CompleteAsync()
    {
        _call.EnsureNotDisposed();

        using (_call.StartScope())
        {
            Log.CompletingClientStream(Logger);

            lock (WriteLock)
            {
                // Pending writes need to be awaited first
                if (IsWriteInProgressUnsynchronized)
                {
                    var ex = new InvalidOperationException("Can't complete the client stream writer because the previous write is in progress.");
                    Log.CompleteClientStreamError(Logger, ex);
                    return Task.FromException(ex);
                }

                // Notify that the client stream is complete
                CompleteTcs.TrySetResult(true);
                _completeCalled = true;
            }
        }

        return Task.CompletedTask;
    }

    public override async Task WriteCoreAsync(TRequest message, CancellationToken cancellationToken)
    {
        ArgumentNullThrowHelper.ThrowIfNull(message);

        _call.TryRegisterCancellation(cancellationToken, out var ctsRegistration);

        try
        {
            await WriteAsync(WriteMessageToStream, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ctsRegistration?.Dispose();
        }

        static Task WriteMessageToStream(GrpcCall<TRequest, TResponse> call, Stream writeStream, CallOptions callOptions, TRequest message)
        {
            return call.WriteMessageAsync(writeStream, message, callOptions);
        }
    }

    public Task WriteAsync<TState>(Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, Task> writeFunc, TState state, CancellationToken cancellationToken)
    {
        _call.EnsureNotDisposed();

        lock (WriteLock)
        {
            using (_call.StartScope())
            {
                // CompleteAsync has already been called
                // Use explicit flag here. This error takes precedence over others.
                if (_completeCalled)
                {
                    return CreateErrorTask("Request stream has already been completed.");
                }

                // Call has already completed
                if (_call.CallTask.IsCompletedSuccessfully())
                {
                    var status = _call.CallTask.Result;
                    if (_call.CancellationToken.IsCancellationRequested &&
                        _call.Channel.ThrowOperationCanceledOnCancellation &&
                        (status.StatusCode == StatusCode.Cancelled || status.StatusCode == StatusCode.DeadlineExceeded))
                    {
                        return Task.FromCanceled(_call.GetCanceledToken(cancellationToken));
                    }

                    return Task.FromException(_call.CreateCanceledStatusException());
                }

                // Pending writes need to be awaited first
                if (IsWriteInProgressUnsynchronized)
                {
                    return CreateErrorTask("Can't write the message because the previous write is in progress.");
                }

                // Save write task to track whether it is complete. Must be set inside lock.
                WriteTask = WriteAsyncCore(writeFunc, state, cancellationToken);
            }
        }

        return WriteTask;
    }

    public GrpcCall<TRequest, TResponse> Call => _call;

    public async Task WriteAsyncCore<TState>(Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, Task> writeFunc, TState state, CancellationToken cancellationToken)
    {
        try
        {
            // Wait until the client stream has started
            var writeStream = await WriteStreamTcs.Task.ConfigureAwait(false);

            // WriteOptions set on the writer take precedence over the CallOptions.WriteOptions
            var callOptions = _call.Options;
            if (WriteOptions != null)
            {
                // Creates a copy of the struct
                callOptions = callOptions.WithWriteOptions(WriteOptions);
            }

            await writeFunc(_call, writeStream, callOptions, state).ConfigureAwait(false);

            // Flush stream to ensure messages are sent immediately.
            await writeStream.FlushAsync(_call.CancellationToken).ConfigureAwait(false);
            if (GrpcEventSource.Log.IsEnabled())
            {
                GrpcEventSource.Log.MessageSent();
            }
        }
        catch (OperationCanceledException ex)
        {
            var resolvedCanceledException = _call.EnsureUserCancellationTokenReported(ex, cancellationToken);
            if (!_call.Channel.ThrowOperationCanceledOnCancellation)
            {
                throw _call.CreateCanceledStatusException(resolvedCanceledException);
            }
            ExceptionDispatchInfo.Capture(resolvedCanceledException).Throw();
        }
    }

    private string DebuggerToString() => $"WriteCount = {_call.MessagesWritten}, WriterCompleted = {(_completeCalled ? "true" : "false")}";

    private sealed class HttpContentClientStreamWriterDebugView
    {
        private readonly HttpContentClientStreamWriter<TRequest, TResponse> _writer;

        public HttpContentClientStreamWriterDebugView(HttpContentClientStreamWriter<TRequest, TResponse> writer)
        {
            _writer = writer;
        }

        public object? Call => _writer._call.CallWrapper;
        public bool WriterCompleted => _writer._completeCalled;
        public long WriteCount => _writer._call.MessagesWritten;
        public WriteOptions? WriteOptions => _writer.WriteOptions;
    }
}
