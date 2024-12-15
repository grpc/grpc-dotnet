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
using Microsoft.Extensions.Logging;
using Log = Grpc.Net.Client.Internal.HttpContentClientStreamReaderLog;

namespace Grpc.Net.Client.Internal;

[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(HttpContentClientStreamReader<,>.HttpContentClientStreamReaderDebugView))]
internal sealed class HttpContentClientStreamReader<TRequest, TResponse> : IAsyncStreamReader<TResponse>
    where TRequest : class
    where TResponse : class
{
    // Getting logger name from generic type is slow. Cached copy.
    private const string LoggerName = "Grpc.Net.Client.Internal.HttpContentClientStreamReader";

    private readonly GrpcCall<TRequest, TResponse> _call;
    private readonly ILogger _logger;
    private readonly object _moveNextLock;

    public TaskCompletionSource<(HttpResponseMessage, Status?)> HttpResponseTcs { get; }

    private HttpResponseMessage? _httpResponse;
    private string? _grpcEncoding;
    private Stream? _responseStream;
    private Task<bool>? _moveNextTask;

    public HttpContentClientStreamReader(GrpcCall<TRequest, TResponse> call)
    {
        _call = call;
        _logger = call.Channel.LoggerFactory.CreateLogger(LoggerName);
        _moveNextLock = new object();

        HttpResponseTcs = new TaskCompletionSource<(HttpResponseMessage, Status?)>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public TResponse Current { get; private set; } = default!;

    public void Dispose()
    {
    }

    public Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        _call.EnsureNotDisposed();

        // HTTP response has finished
        if (_call.CancellationToken.IsCancellationRequested)
        {
            if (!_call.Channel.ThrowOperationCanceledOnCancellation)
            {
                var ex = _call.CreateCanceledStatusException(new OperationCanceledException(_call.CancellationToken));
                return Task.FromException<bool>(ex);
            }
            else
            {
                if (_call.Options.CancellationToken.IsCancellationRequested)
                {
                    return Task.FromCanceled<bool>(_call.Options.CancellationToken);
                }

                return Task.FromCanceled<bool>(_call.CancellationToken);
            }
        }

        if (_call.CallTask.IsCompletedSuccessfully())
        {
            var status = _call.CallTask.Result;
            if (status.StatusCode == StatusCode.OK)
            {
                // Response is finished and it was successful so just return false
                return CommonGrpcProtocolHelpers.FalseTask;
            }
            else
            {
                return Task.FromException<bool>(_call.CreateRpcException(status));
            }
        }

        lock (_moveNextLock)
        {
            using (_call.StartScope())
            {
                // Pending move next need to be awaited first
                if (IsMoveNextInProgressUnsynchronized)
                {
                    var ex = new InvalidOperationException("Can't read the next message because the previous read is still in progress.");
                    Log.ReadMessageError(_logger, ex);
                    return Task.FromException<bool>(ex);
                }

                // Save move next task to track whether it is complete
                _moveNextTask = MoveNextCore(cancellationToken);
            }
        }

        return _moveNextTask;
    }

    private async Task<bool> MoveNextCore(CancellationToken cancellationToken)
    {
        _call.TryRegisterCancellation(cancellationToken, out var ctsRegistration);
        try
        {
            _call.CancellationToken.ThrowIfCancellationRequested();

            if (_httpResponse == null)
            {
                var (httpResponse, status) = await HttpResponseTcs.Task.ConfigureAwait(false);
                if (status != null && status.Value.StatusCode != StatusCode.OK)
                {
                    throw _call.CreateFailureStatusException(status.Value);
                }

                _httpResponse = httpResponse;
                _grpcEncoding = GrpcProtocolHelpers.GetGrpcEncoding(_httpResponse);
            }
            if (_responseStream == null)
            {
                try
                {
#if NET5_0_OR_GREATER
                    _responseStream = await _httpResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
#else
                    _responseStream = await _httpResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
                }
                catch (ObjectDisposedException)
                {
                    // The response was disposed while waiting for the content stream to start.
                    // This will happen if there is no content stream (e.g. a streaming call finishes with no messages).
                    // Treat this like a cancellation.
                    throw new OperationCanceledException();
                }
            }

            CompatibilityHelpers.Assert(_grpcEncoding != null, "Encoding should have been calculated from response.");

            // Clear current before moving next. This prevents rooting the previous value while getting the next one.
            // In a long running stream this can allow the previous value to be GCed.
            Current = null!;

            var readMessage = await _call.ReadMessageAsync(
                _responseStream,
                _grpcEncoding,
                singleMessage: false,
                _call.CancellationToken).ConfigureAwait(false);

            if (readMessage == null)
            {
                // No more content in response so report status to call.
                // The call will handle finishing the response.
                var status = GrpcProtocolHelpers.GetResponseStatus(_httpResponse, _call.Channel.OperatingSystem.IsBrowser, _call.Channel.HttpHandlerType == HttpHandlerType.WinHttpHandler);
                _call.ResponseStreamEnded(status, finishedGracefully: true);
                if (status.StatusCode != StatusCode.OK)
                {
                    throw _call.CreateFailureStatusException(status);
                }

                Current = null!;
                return false;
            }
            if (GrpcEventSource.Log.IsEnabled())
            {
                GrpcEventSource.Log.MessageReceived();
            }
            Current = readMessage!;
            return true;
        }
        catch (OperationCanceledException ex)
        {
            var resolvedCanceledException = _call.EnsureUserCancellationTokenReported(ex, cancellationToken);

            if (_call.ResponseFinished)
            {
                // Call status will have been set before dispose.
                var status = await _call.CallTask.ConfigureAwait(false);
                if (status.StatusCode == StatusCode.OK)
                {
                    // Return false to indicate that the stream is complete without a message.
                    return false;
                }
                else if (status.StatusCode != StatusCode.DeadlineExceeded && status.StatusCode != StatusCode.Cancelled)
                {
                    throw _call.CreateRpcException(status);
                }
            }
            else
            {
                _call.ResponseStreamEnded(new Status(StatusCode.Cancelled, ex.Message, resolvedCanceledException), finishedGracefully: false);
            }

            if (!_call.Channel.ThrowOperationCanceledOnCancellation)
            {
                throw _call.CreateCanceledStatusException(resolvedCanceledException);
            }
            else
            {
                ExceptionDispatchInfo.Capture(resolvedCanceledException).Throw();
                return false; // Won't reach here.
            }
        }
        catch (Exception ex)
        {
            var newException = _call.ResolveException("Error reading next message.", ex, out var status, out var resolvedException);
            if (!_call.ResponseFinished)
            {
                _call.ResponseStreamEnded(status.Value, finishedGracefully: false);
            }

            if (newException)
            {
                // Throw RpcException from MoveNext. Consistent with Grpc.Core.
                throw resolvedException;
            }
            else
            {
                throw;
            }
        }
        finally
        {
            ctsRegistration?.Dispose();
        }
    }

    /// <summary>
    /// A value indicating whether there is an async move next already in progress.
    /// Should only check this property when holding the move next lock.
    /// </summary>
    private bool IsMoveNextInProgressUnsynchronized
    {
        get
        {
            var moveNextTask = _moveNextTask;
            return moveNextTask != null && !moveNextTask.IsCompleted;
        }
    }

    private string DebuggerToString() => $"ReadCount = {_call.MessagesRead}, EndOfStream = {(_call.ResponseFinished ? "true" : "false")}";

    private sealed class HttpContentClientStreamReaderDebugView
    {
        private readonly HttpContentClientStreamReader<TRequest, TResponse> _reader;

        public HttpContentClientStreamReaderDebugView(HttpContentClientStreamReader<TRequest, TResponse> reader)
        {
            _reader = reader;
        }

        public object? Call => _reader._call.CallWrapper;
        public long ReadCount => _reader._call.MessagesRead;
        public TResponse Current => _reader.Current;
        public bool EndOfStream => _reader._call.ResponseFinished;
    }
}

internal static partial class HttpContentClientStreamReaderLog
{
    [LoggerMessage(Level = LogLevel.Error, EventId = 1, EventName = "ReadMessageError", Message = "Error reading message.")]
    public static partial void ReadMessageError(ILogger logger, Exception ex);
}
