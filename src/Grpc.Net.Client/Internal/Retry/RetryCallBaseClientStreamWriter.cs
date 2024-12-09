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
using Log = Grpc.Net.Client.Internal.ClientStreamWriterBaseLog;

namespace Grpc.Net.Client.Internal.Retry;

[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(RetryCallBaseClientStreamWriter<,>.RetryCallBaseClientStreamWriterDebugView))]
internal sealed class RetryCallBaseClientStreamWriter<TRequest, TResponse> : ClientStreamWriterBase<TRequest>
    where TRequest : class
    where TResponse : class
{
    // Getting logger name from generic type is slow
    private const string LoggerName = "Grpc.Net.Client.Internal.Retry.RetryCallBaseClientStreamWriter";

    private readonly RetryCallBase<TRequest, TResponse> _retryCallBase;

    public RetryCallBaseClientStreamWriter(RetryCallBase<TRequest, TResponse> retryCallBase)
        : base(retryCallBase.Channel.LoggerFactory.CreateLogger(LoggerName))
    {
        _retryCallBase = retryCallBase;
    }

    public override WriteOptions? WriteOptions
    {
        get => _retryCallBase.ClientStreamWriteOptions;
        set => _retryCallBase.ClientStreamWriteOptions = value;
    }

    public override Task CompleteAsync()
    {
        lock (WriteLock)
        {
            // Pending writes need to be awaited first
            if (IsWriteInProgressUnsynchronized)
            {
                var ex = new InvalidOperationException("Can't complete the client stream writer because the previous write is in progress.");
                Log.CompleteClientStreamError(Logger, ex);
                return Task.FromException(ex);
            }

            return _retryCallBase.ClientStreamCompleteAsync();
        }
    }

    public override Task WriteCoreAsync(TRequest message, CancellationToken cancellationToken)
    {
        lock (WriteLock)
        {
            // CompleteAsync has already been called
            // Use explicit flag here. This error takes precedence over others.
            if (_retryCallBase.ClientStreamComplete)
            {
                return CreateErrorTask("Request stream has already been completed.");
            }

            // Pending writes need to be awaited first
            if (IsWriteInProgressUnsynchronized)
            {
                return CreateErrorTask("Can't write the message because the previous write is in progress.");
            }

            // Save write task to track whether it is complete. Must be set inside lock.
            WriteTask = _retryCallBase.ClientStreamWriteAsync(message, cancellationToken);
        }

        return WriteTask;
    }

    private string DebuggerToString() => $"WriteCount = {_retryCallBase.MessagesWritten}, WriterCompleted = {(_retryCallBase.ClientStreamComplete ? "true" : "false")}";

    private sealed class RetryCallBaseClientStreamWriterDebugView
    {
        private readonly RetryCallBaseClientStreamWriter<TRequest, TResponse> _writer;

        public RetryCallBaseClientStreamWriterDebugView(RetryCallBaseClientStreamWriter<TRequest, TResponse> writer)
        {
            _writer = writer;
        }

        public object? Call => _writer._retryCallBase.CallWrapper;
        public bool WriterCompleted => _writer._retryCallBase.ClientStreamComplete;
        public long WriteCount => _writer._retryCallBase.MessagesWritten;
        public WriteOptions? WriteOptions => _writer.WriteOptions;
    }
}
