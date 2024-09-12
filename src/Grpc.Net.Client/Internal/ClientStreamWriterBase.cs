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
using Microsoft.Extensions.Logging;
using Log = Grpc.Net.Client.Internal.ClientStreamWriterBaseLog;

namespace Grpc.Net.Client.Internal;

internal abstract class ClientStreamWriterBase<TRequest> : IClientStreamWriter<TRequest>
    where TRequest : class
{
    protected ILogger Logger { get; }
    protected object WriteLock { get; }
    protected Task? WriteTask { get; set; }

    protected ClientStreamWriterBase(ILogger logger)
    {
        Logger = logger;
        WriteLock = new object();
    }

    public abstract WriteOptions? WriteOptions { get; set; }

    public abstract Task CompleteAsync();

    public Task WriteAsync(TRequest message) => WriteCoreAsync(message, CancellationToken.None);

#if NETSTANDARD2_1_OR_GREATER || NET5_0_OR_GREATER
    // Explicit implementation because this WriteAsync has a default interface implementation.
    Task IAsyncStreamWriter<TRequest>.WriteAsync(TRequest message, CancellationToken cancellationToken)
    {
        return WriteCoreAsync(message, cancellationToken);
    }
#endif

    public abstract Task WriteCoreAsync(TRequest message, CancellationToken cancellationToken);

    protected Task CreateErrorTask(string message)
    {
        var ex = new InvalidOperationException(message);
        Log.WriteMessageError(Logger, ex);
        return Task.FromException(ex);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// A value indicating whether there is an async write already in progress.
    /// Should only check this property when holding the write lock.
    /// </summary>
    protected bool IsWriteInProgressUnsynchronized
    {
        get
        {
            var writeTask = WriteTask;
            return writeTask != null && !writeTask.IsCompleted;
        }
    }
}

internal static partial class ClientStreamWriterBaseLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "CompletingClientStream", Message = "Completing client stream.")]
    public static partial void CompletingClientStream(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, EventId = 2, EventName = "WriteMessageError", Message = "Error writing message.")]
    public static partial void WriteMessageError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, EventId = 3, EventName = "CompleteClientStreamError", Message = "Error completing client stream.")]
    public static partial void CompleteClientStreamError(ILogger logger, Exception ex);
}
