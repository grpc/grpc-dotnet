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
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Internal
{
    internal class HttpContentClientStreamWriter<TRequest, TResponse> : IClientStreamWriter<TRequest>
        where TRequest : class
        where TResponse : class
    {
        // Getting logger name from generic type is slow
        private const string LoggerName = "Grpc.Net.Client.Internal.HttpContentClientStreamWriter";

        private readonly GrpcCall<TRequest, TResponse> _call;
        private readonly ILogger _logger;
        private readonly object _writeLock;
        private Task? _writeTask;

        public TaskCompletionSource<Stream> WriteStreamTcs { get; }
        public TaskCompletionSource<bool> CompleteTcs { get; }

        public HttpContentClientStreamWriter(GrpcCall<TRequest, TResponse> call)
        {
            _call = call;
            _logger = call.Channel.LoggerFactory.CreateLogger(LoggerName);

            WriteStreamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            CompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _writeLock = new object();
            WriteOptions = _call.Options.WriteOptions;
        }

        public WriteOptions WriteOptions { get; set; }

        public Task CompleteAsync()
        {
            _call.EnsureNotDisposed();

            using (_call.StartScope())
            {
                Log.CompletingClientStream(_logger);

                lock (_writeLock)
                {
                    // Pending writes need to be awaited first
                    if (IsWriteInProgressUnsynchronized)
                    {
                        var ex = new InvalidOperationException("Can't complete the client stream writer because the previous write is in progress.");
                        Log.CompleteClientStreamError(_logger, ex);
                        return Task.FromException(ex);
                    }

                    // Notify that the client stream is complete
                    CompleteTcs.TrySetResult(true);
                }
            }

            return Task.CompletedTask;
        }

        public Task WriteAsync(TRequest message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            _call.EnsureNotDisposed();

            lock (_writeLock)
            {
                using (_call.StartScope())
                {
                    // Call has already completed
                    if (_call.CallTask.IsCompletedSuccessfully())
                    {
                        var status = _call.CallTask.Result;
                        if (_call.CancellationToken.IsCancellationRequested &&
                            (status.StatusCode == StatusCode.Cancelled || status.StatusCode == StatusCode.DeadlineExceeded))
                        {
                            if (!_call.Channel.ThrowOperationCanceledOnCancellation)
                            {
                                return Task.FromException(_call.CreateCanceledStatusException());
                            }
                            else
                            {
                                return Task.FromCanceled(_call.CancellationToken);
                            }
                        }

                        return CreateErrorTask("Can't write the message because the call is complete.");
                    }

                    // CompleteAsync has already been called
                    // Use IsCompleted here because that will track success and cancellation
                    if (CompleteTcs.Task.IsCompleted)
                    {
                        return CreateErrorTask("Can't write the message because the client stream writer is complete.");
                    }
                    
                    // Pending writes need to be awaited first
                    if (IsWriteInProgressUnsynchronized)
                    {
                        return CreateErrorTask("Can't write the message because the previous write is in progress.");
                    }

                    // Save write task to track whether it is complete. Must be set inside lock.
                    _writeTask = WriteAsyncCore(message);
                }
            }

            return _writeTask;
        }

        private Task CreateErrorTask(string message)
        {
            var ex = new InvalidOperationException(message);
            Log.WriteMessageError(_logger, ex);
            return Task.FromException(ex);
        }

        public void Dispose()
        {
        }

        private async Task WriteAsyncCore(TRequest message)
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

                await _call.WriteMessageAsync(
                    writeStream,
                    message,
                    _call.Method.RequestMarshaller.ContextualSerializer,
                    callOptions).ConfigureAwait(false);

                // Flush stream to ensure messages are sent immediately
                await writeStream.FlushAsync(callOptions.CancellationToken).ConfigureAwait(false);

                GrpcEventSource.Log.MessageSent();
            }
            catch (OperationCanceledException) when (!_call.Channel.ThrowOperationCanceledOnCancellation)
            {
                throw _call.CreateCanceledStatusException();
            }
        }

        /// <summary>
        /// A value indicating whether there is an async write already in progress.
        /// Should only check this property when holding the write lock.
        /// </summary>
        private bool IsWriteInProgressUnsynchronized
        {
            get
            {
                var writeTask = _writeTask;
                return writeTask != null && !writeTask.IsCompleted;
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception?> _completingClientStream =
                LoggerMessage.Define(LogLevel.Debug, new EventId(1, "CompletingClientStream"), "Completing client stream.");

            private static readonly Action<ILogger, Exception?> _writeMessageError =
                LoggerMessage.Define(LogLevel.Error, new EventId(2, "WriteMessageError"), "Error writing message.");

            private static readonly Action<ILogger, Exception?> _completeClientStreamError =
                LoggerMessage.Define(LogLevel.Error, new EventId(3, "CompleteClientStreamError"), "Error completing client stream.");

            public static void CompletingClientStream(ILogger logger)
            {
                _completingClientStream(logger, null);
            }

            public static void WriteMessageError(ILogger logger, Exception ex)
            {
                _writeMessageError(logger, ex);
            }

            public static void CompleteClientStreamError(ILogger logger, Exception ex)
            {
                _completeClientStreamError(logger, ex);
            }
        }
    }
}
