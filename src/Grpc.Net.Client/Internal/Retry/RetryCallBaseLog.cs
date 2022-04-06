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

namespace Grpc.Net.Client.Internal.Retry
{
    internal static class RetryCallBaseLog
    {
        private static readonly Action<ILogger, StatusCode, int, bool, Exception?> _retryEvaluated =
            LoggerMessage.Define<StatusCode, int, bool>(LogLevel.Debug, new EventId(1, "RetryEvaluated"), "Evaluated retry for failed gRPC call. Status code: '{StatusCode}', Attempt: {AttemptCount}, Retry: {WillRetry}");

        private static readonly Action<ILogger, string, Exception?> _retryPushbackReceived =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, "RetryPushbackReceived"), "Retry pushback of '{RetryPushback}' received from the failed gRPC call.");

        private static readonly Action<ILogger, TimeSpan, Exception?> _startingRetryDelay =
            LoggerMessage.Define<TimeSpan>(LogLevel.Trace, new EventId(3, "StartingRetryDelay"), "Starting retry delay of {DelayDuration}.");

        private static readonly Action<ILogger, Exception> _errorRetryingCall =
            LoggerMessage.Define(LogLevel.Error, new EventId(4, "ErrorRetryingCall"), "Error retrying gRPC call.");

        private static readonly Action<ILogger, int, Exception?> _sendingBufferedMessages =
            LoggerMessage.Define<int>(LogLevel.Trace, new EventId(5, "SendingBufferedMessages"), "Sending {MessageCount} buffered messages from previous failed gRPC calls.");

        private static readonly Action<ILogger, int, long, Exception?> _messageAddedToBuffer =
            LoggerMessage.Define<int, long>(LogLevel.Trace, new EventId(6, "MessageAddedToBuffer"), "Message with {MessageSize} bytes added to the buffer. There are {CallBufferSize} bytes buffered for this call.");

        private static readonly Action<ILogger, CommitReason, Exception?> _callCommited =
            LoggerMessage.Define<CommitReason>(LogLevel.Debug, new EventId(7, "CallCommited"), "Call commited. Reason: {CommitReason}");

        private static readonly Action<ILogger, Exception?> _startingRetryWorker =
            LoggerMessage.Define(LogLevel.Trace, new EventId(8, "StartingRetryWorker"), "Starting retry worker.");

        private static readonly Action<ILogger, Exception?> _stoppingRetryWorker =
            LoggerMessage.Define(LogLevel.Trace, new EventId(9, "StoppingRetryWorker"), "Stopping retry worker.");

        private static readonly Action<ILogger, int, int, Exception?> _maxAttemptsLimited =
            LoggerMessage.Define<int, int>(LogLevel.Debug, new EventId(10, "MaxAttemptsLimited"), "The method has {ServiceConfigMaxAttempts} attempts specified in the service config. The number of attempts has been limited by channel configuration to {ChannelMaxAttempts}.");

        private static readonly Action<ILogger, Exception?> _additionalCallsBlockedByRetryThrottling =
            LoggerMessage.Define(LogLevel.Debug, new EventId(11, "AdditionalCallsBlockedByRetryThrottling"), "Additional calls blocked by retry throttling.");

        private static readonly Action<ILogger, int, Exception?> _startingAttempt =
            LoggerMessage.Define<int>(LogLevel.Debug, new EventId(12, "StartingAttempt"), "Starting attempt {AttemptCount}.");

        private static readonly Action<ILogger, Exception?> _canceledRetry =
            LoggerMessage.Define(LogLevel.Debug, new EventId(13, "CanceledRetry"), "gRPC retry call canceled.");

        internal static void RetryEvaluated(ILogger logger, StatusCode statusCode, int attemptCount, bool willRetry)
        {
            _retryEvaluated(logger, statusCode, attemptCount, willRetry, null);
        }

        internal static void RetryPushbackReceived(ILogger logger, string retryPushback)
        {
            _retryPushbackReceived(logger, retryPushback, null);
        }

        internal static void StartingRetryDelay(ILogger logger, TimeSpan delayDuration)
        {
            _startingRetryDelay(logger, delayDuration, null);
        }

        internal static void ErrorRetryingCall(ILogger logger, Exception ex)
        {
            _errorRetryingCall(logger, ex);
        }

        internal static void SendingBufferedMessages(ILogger logger, int messageCount)
        {
            _sendingBufferedMessages(logger, messageCount, null);
        }

        internal static void MessageAddedToBuffer(ILogger logger, int messageSize, long callBufferSize)
        {
            _messageAddedToBuffer(logger, messageSize, callBufferSize, null);
        }

        internal static void CallCommited(ILogger logger, CommitReason commitReason)
        {
            _callCommited(logger, commitReason, null);
        }

        internal static void StartingRetryWorker(ILogger logger)
        {
            _startingRetryWorker(logger, null);
        }

        internal static void StoppingRetryWorker(ILogger logger)
        {
            _stoppingRetryWorker(logger, null);
        }

        internal static void MaxAttemptsLimited(ILogger logger, int serviceConfigMaxAttempts, int channelMaxAttempts)
        {
            _maxAttemptsLimited(logger, serviceConfigMaxAttempts, channelMaxAttempts, null);
        }

        internal static void AdditionalCallsBlockedByRetryThrottling(ILogger logger)
        {
            _additionalCallsBlockedByRetryThrottling(logger, null);
        }

        internal static void StartingAttempt(ILogger logger, int attempts)
        {
            _startingAttempt(logger, attempts, null);
        }

        internal static void CanceledRetry(ILogger logger)
        {
            _canceledRetry(logger, null);
        }
    }
}
