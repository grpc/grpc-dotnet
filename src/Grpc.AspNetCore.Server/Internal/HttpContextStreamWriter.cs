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

using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextStreamWriter<TResponse> : IServerStreamWriter<TResponse>
        where TResponse : class
    {
        private readonly HttpContextServerCallContext _context;
        private readonly Action<TResponse, SerializationContext> _serializer;
        private readonly object _writeLock;
        private Task? _writeTask;
        private bool _completed;

        public HttpContextStreamWriter(HttpContextServerCallContext context, Action<TResponse, SerializationContext> serializer)
        {
            _context = context;
            _serializer = serializer;
            _writeLock = new object();
        }

        public WriteOptions? WriteOptions
        {
            get => _context.WriteOptions;
            set => _context.WriteOptions = value;
        }

        public Task WriteAsync(TResponse message)
        {
            return WriteCoreAsync(message, CancellationToken.None);
        }

#if NET5_0_OR_GREATER
        // Explicit implementation because this WriteAsync has a default interface implementation.
        Task IAsyncStreamWriter<TResponse>.WriteAsync(TResponse message, CancellationToken cancellationToken)
        {
            return WriteCoreAsync(message, cancellationToken);
        }
#endif

        private async Task WriteCoreAsync(TResponse message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            CancellationTokenRegistration? registration = null;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(
                    static (state) => ((HttpContextServerCallContext)state!).CancelRequest(),
                    _context);
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (_completed || _context.CancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException("Can't write the message because the request is complete.");
                }

                lock (_writeLock)
                {
                    // Pending writes need to be awaited first
                    if (IsWriteInProgressUnsynchronized)
                    {
                        throw new InvalidOperationException("Can't write the message because the previous write is in progress.");
                    }

                    // Save write task to track whether it is complete. Must be set inside lock.
                    _writeTask = _context.HttpContext.Response.BodyWriter.WriteMessageAsync(message, _context, _serializer, canFlush: true);
                }

                await _writeTask;
            }
            finally
            {
                registration?.Dispose();
            }
        }

        public void Complete()
        {
            _completed = true;
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
