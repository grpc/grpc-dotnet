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

namespace Grpc.Net.Client.Internal.Retry;

internal static partial class RetryCallBaseLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "RetryEvaluated", Message = "Evaluated retry for failed gRPC call. Status code: '{StatusCode}', Attempt: {AttemptCount}, Retry: {WillRetry}")]
    internal static partial void RetryEvaluated(ILogger logger, StatusCode statusCode, int attemptCount, bool willRetry);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 2, EventName = "RetryPushbackReceived", Message = "Retry pushback of '{RetryPushback}' received from the failed gRPC call.")]
    internal static partial void RetryPushbackReceived(ILogger logger, string retryPushback);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 3, EventName = "StartingRetryDelay", Message = "Starting retry delay of {DelayDuration}.")]
    internal static partial void StartingRetryDelay(ILogger logger, TimeSpan delayDuration);

    [LoggerMessage(Level = LogLevel.Error, EventId = 4, EventName = "ErrorRetryingCall", Message = "Error retrying gRPC call.")]
    internal static partial void ErrorRetryingCall(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 5, EventName = "SendingBufferedMessages", Message = "Sending {MessageCount} buffered messages from previous failed gRPC calls.")]
    internal static partial void SendingBufferedMessages(ILogger logger, int messageCount);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 6, EventName = "MessageAddedToBuffer", Message = "Message with {MessageSize} bytes added to the buffer. There are {CallBufferSize} bytes buffered for this call.")]
    internal static partial void MessageAddedToBuffer(ILogger logger, int messageSize, long callBufferSize);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 7, EventName = "CallCommited", Message = "Call commited. Reason: {CommitReason}")]
    internal static partial void CallCommited(ILogger logger, CommitReason commitReason);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 8, EventName = "StartingRetryWorker", Message = "Starting retry worker.")]
    internal static partial void StartingRetryWorker(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 9, EventName = "StoppingRetryWorker", Message = "Stopping retry worker.")]
    internal static partial void StoppingRetryWorker(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 10, EventName = "MaxAttemptsLimited", Message = "The method has {ServiceConfigMaxAttempts} attempts specified in the service config. The number of attempts has been limited by channel configuration to {ChannelMaxAttempts}.")]
    internal static partial void MaxAttemptsLimited(ILogger logger, int serviceConfigMaxAttempts, int channelMaxAttempts);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 11, EventName = "AdditionalCallsBlockedByRetryThrottling", Message = "Additional calls blocked by retry throttling.")]
    internal static partial void AdditionalCallsBlockedByRetryThrottling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 12, EventName = "StartingAttempt", Message = "Starting attempt {AttemptCount}.")]
    internal static partial void StartingAttempt(ILogger logger, int AttemptCount);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 13, EventName = "CanceledRetry", Message = "gRPC retry call canceled.")]
    internal static partial void CanceledRetry(ILogger logger);
}
