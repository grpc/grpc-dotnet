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
using Microsoft.Extensions.Logging;
using Log = Grpc.Net.Client.Internal.ClientStreamWriterBaseLog;

namespace Grpc.Net.Client.Internal
{
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

        // TODO(JamesNK): Remove nullable override after Grpc.Core.Api update
#pragma warning disable CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
        public abstract WriteOptions? WriteOptions { get; set; }
#pragma warning restore CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).

        public abstract Task CompleteAsync();

        public abstract Task WriteAsync(TRequest message);

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
                Debug.Assert(Monitor.IsEntered(WriteLock));

                var writeTask = WriteTask;
                return writeTask != null && !writeTask.IsCompleted;
            }
        }
    }

    internal static class ClientStreamWriterBaseLog
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
