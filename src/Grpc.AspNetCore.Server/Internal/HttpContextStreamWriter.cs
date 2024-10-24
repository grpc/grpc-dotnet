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
using System.IO.Pipelines;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.AspNetCore.Http.Features;

namespace Grpc.AspNetCore.Server.Internal;

[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(HttpContextStreamWriter<>.HttpContextStreamWriterDebugView))]
internal sealed class HttpContextStreamWriter<TResponse> : IServerStreamWriter<TResponse>
    where TResponse : class
{
    private readonly HttpContextServerCallContext _context;
    private readonly Action<TResponse, SerializationContext> _serializer;
    private readonly PipeWriter _bodyWriter;
    private readonly IHttpRequestLifetimeFeature _requestLifetimeFeature;
    private readonly object _writeLock;
    private Task? _writeTask;
    private bool _completed;
    private long _writeCount;

    public HttpContextStreamWriter(HttpContextServerCallContext context, Action<TResponse, SerializationContext> serializer)
    {
        _context = context;
        _serializer = serializer;
        _writeLock = new object();

        // Copy HttpContext values.
        // This is done to avoid a race condition when reading them from HttpContext later when running in a separate thread.
        _bodyWriter = context.HttpContext.Response.BodyWriter;
        // Copy lifetime feature because HttpContext.RequestAborted on .NET 6 doesn't return the real cancellation token.
        _requestLifetimeFeature = GrpcProtocolHelpers.GetRequestLifetimeFeature(context.HttpContext);
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

    // Explicit implementation because this WriteAsync has a default interface implementation.
    Task IAsyncStreamWriter<TResponse>.WriteAsync(TResponse message, CancellationToken cancellationToken)
    {
        return WriteCoreAsync(message, cancellationToken);
    }

    private async Task WriteCoreAsync(TResponse message, CancellationToken cancellationToken)
    {
        ArgumentNullThrowHelper.ThrowIfNull(message);

        // Register cancellation token early to ensure request is canceled if cancellation is requested.
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

            if (_completed || _requestLifetimeFeature.RequestAborted.IsCancellationRequested)
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
                _writeTask = _bodyWriter.WriteStreamedMessageAsync(message, _context, _serializer, cancellationToken);
            }

            await _writeTask;
            Interlocked.Increment(ref _writeCount);
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

    private string DebuggerToString() => $"WriteCount = {_writeCount}, WriterCompleted = {(_completed ? "true" : "false")}";

    private sealed class HttpContextStreamWriterDebugView
    {
        private readonly HttpContextStreamWriter<TResponse> _writer;

        public HttpContextStreamWriterDebugView(HttpContextStreamWriter<TResponse> writer)
        {
            _writer = writer;
        }

        public ServerCallContext ServerCallContext => _writer._context;
        public bool WriterCompleted => _writer._completed;
        public long WriteCount => _writer._writeCount;
        public WriteOptions? WriteOptions => _writer.WriteOptions;
    }
}
