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
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.NetCore.HttpClient.Internal
{
    internal class HttpContentClientStreamWriter<TRequest, TResponse> : IClientStreamWriter<TRequest>
    {
        private readonly GrpcCall<TRequest, TResponse> _call;
        private readonly Task<Stream> _writeStreamTask;
        private readonly TaskCompletionSource<bool> _completeTcs;
        private readonly object _writeLock;
        private Task _writeTask;

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
            lock (_writeLock)
            {
                // Pending writes need to be awaited first
                if (IsWriteInProgressUnsynchronized)
                {
                    return Task.FromException(new InvalidOperationException("Cannot complete client stream writer because the previous write is in progress."));
                }

                // Notify that the client stream is complete
                _completeTcs.TrySetResult(true);
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
                // CompleteAsync has already been called
                if (_completeTcs.Task.IsCompletedSuccessfully)
                {
                    return Task.FromException(new InvalidOperationException("Cannot write message because the client stream writer is complete."));
                }
                else if (_completeTcs.Task.IsCanceled)
                {
                    throw _call.CreateCanceledStatusException();
                }

                // Pending writes need to be awaited first
                if (IsWriteInProgressUnsynchronized)
                {
                    return Task.FromException(new InvalidOperationException("Cannot write message because the previous write is in progress."));
                }

                // Save write task to track whether it is complete
                _writeTask = WriteAsyncCore(message);
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

                await SerializationHelpers.WriteMessage<TRequest>(writeStream, message, _call.Method.RequestMarshaller.Serializer, _call.CancellationToken).ConfigureAwait(false);
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
    }
}
