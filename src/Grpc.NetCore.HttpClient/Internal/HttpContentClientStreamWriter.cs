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
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Grpc.NetCore.HttpClient.Internal
{
    internal class HttpContentClientStreamWriter<TRequest, TResponse> : IClientStreamWriter<TRequest>
        where TRequest : class
        where TResponse : class
    {
        private readonly GrpcCall<TRequest, TResponse> _call;
        private readonly Task<Stream> _writeStreamTask;
        private readonly TaskCompletionSource<bool> _completeTcs;
        private readonly object _writeLock;
        private Task? _writeTask;

        public HttpContentClientStreamWriter(GrpcCall<TRequest, TResponse> call, Task<Stream> writeStreamTask, TaskCompletionSource<bool> completeTcs)
        {
            _call = call;
            _writeStreamTask = writeStreamTask;
            _completeTcs = completeTcs;
            _writeLock = new object();
            WriteOptions = _call.Options.WriteOptions;
        }

        public WriteOptions WriteOptions { get; set; }

        public Task CompleteAsync()
        {
            using (_call.StartScope())
            {
                Log.CompletingClientStream(_call.Logger);

                lock (_writeLock)
                {
                    // Pending writes need to be awaited first
                    if (IsWriteInProgressUnsynchronized)
                    {
                        var ex = new InvalidOperationException("Cannot complete client stream writer because the previous write is in progress.");
                        Log.CompleteClientStreamError(_call.Logger, ex);
                        return Task.FromException(ex);
                    }

                    // Notify that the client stream is complete
                    _completeTcs.TrySetResult(true);
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

            lock (_writeLock)
            {
                using (_call.StartScope())
                {
                    // CompleteAsync has already been called
                    if (_completeTcs.Task.IsCompletedSuccessfully)
                    {
                        var ex = new InvalidOperationException("Cannot write message because the client stream writer is complete.");
                        Log.WriteMessageError(_call.Logger, ex);
                        return Task.FromException(ex);
                    }
                    else if (_completeTcs.Task.IsCanceled)
                    {
                        throw _call.CreateCanceledStatusException();
                    }

                    // Pending writes need to be awaited first
                    if (IsWriteInProgressUnsynchronized)
                    {
                        var ex = new InvalidOperationException("Cannot write message because the previous write is in progress.");
                        Log.WriteMessageError(_call.Logger, ex);
                        return Task.FromException(ex);
                    }

                    // Save write task to track whether it is complete
                    _writeTask = WriteAsyncCore(message);
                }
            }

            return _writeTask;
        }

        public void Dispose()
        {
        }

        private async Task WriteAsyncCore(TRequest message)
        {
            try
            {
                    // Wait until the client stream has started
                    var writeStream = await _writeStreamTask.ConfigureAwait(false);

                    await writeStream.WriteMessage<TRequest>(_call.Logger, message, _call.Method.RequestMarshaller.Serializer, _call.CancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
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
