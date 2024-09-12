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

namespace Grpc.Net.Client.Internal;

internal static partial class GrpcCallLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "StartingCall", Message = "Starting gRPC call. Method type: '{MethodType}', URI: '{Uri}'.")]
    public static partial void StartingCall(ILogger logger, MethodType methodType, Uri uri);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 2, EventName = "ResponseHeadersReceived", Message = "Response headers received.")]
    public static partial void ResponseHeadersReceived(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 3, EventName = "GrpcStatusError", Message = "Call failed with gRPC error status. Status code: '{StatusCode}', Message: '{StatusMessage}'.")]
    public static partial void GrpcStatusError(ILogger logger, StatusCode statusCode, string statusMessage, Exception? debugException);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 4, EventName = "FinishedCall", Message = "Finished gRPC call.")]
    public static partial void FinishedCall(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 5, EventName = "StartingDeadlineTimeout", Message = "Starting deadline timeout. Duration: {DeadlineTimeout}.")]
    public static partial void StartingDeadlineTimeout(ILogger logger, TimeSpan deadlineTimeout);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 6, EventName = "ErrorStartingCall", Message = "Error starting gRPC call.")]
    public static partial void ErrorStartingCall(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, EventId = 7, EventName = "DeadlineExceeded", Message = "gRPC call deadline exceeded.")]
    public static partial void DeadlineExceeded(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 8, EventName = "CanceledCall", Message = "gRPC call canceled.")]
    public static partial void CanceledCall(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 9, EventName = "MessageNotReturned", Message = "Message not returned from unary or client streaming call.")]
    public static partial void MessageNotReturned(ILogger logger);

    // 10, 11 unused.

    [LoggerMessage(Level = LogLevel.Warning, EventId = 12, EventName = "CallCredentialsNotUsed", Message = "The configured CallCredentials were not used because the call does not use TLS.")]
    public static partial void CallCredentialsNotUsed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 13, EventName = "ReadingMessage", Message = "Reading message.")]
    public static partial void ReadingMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 14, EventName = "NoMessageReturned", Message = "No message returned.")]
    public static partial void NoMessageReturned(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 15, EventName = "DeserializingMessage", Message = "Deserializing {MessageLength} byte message to '{MessageType}'.")]
    public static partial void DeserializingMessage(ILogger logger, int messageLength, Type messageType);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 16, EventName = "ReceivedMessage", Message = "Received message.")]
    public static partial void ReceivedMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 17, EventName = "ErrorReadingMessage", Message = "Error reading message.")]
    public static partial void ErrorReadingMessage(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 18, EventName = "SendingMessage", Message = "Sending message.")]
    public static partial void SendingMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 19, EventName = "MessageSent", Message = "Message sent.")]
    public static partial void MessageSent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 20, EventName = "ErrorSendingMessage", Message = "Error sending message.")]
    public static partial void ErrorSendingMessage(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 21, EventName = "SerializedMessage", Message = "Serialized '{MessageType}' to {MessageLength} byte message.")]
    public static partial void SerializedMessage(ILogger logger, Type messageType, int messageLength);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 22, EventName = "CompressingMessage", Message = "Compressing message with '{MessageEncoding}' encoding.")]
    public static partial void CompressingMessage(ILogger logger, string messageEncoding);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 23, EventName = "DecompressingMessage", Message = "Decompressing message with '{MessageEncoding}' encoding.")]
    public static partial void DecompressingMessage(ILogger logger, string messageEncoding);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 24, EventName = "DeadlineTimeoutTooLong", Message = "Deadline timeout {Timeout} is above maximum allowed timeout of 99999999 seconds. Maximum timeout will be used.")]
    public static partial void DeadlineTimeoutTooLong(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 25, EventName = "DeadlineTimerRescheduled", Message = "Deadline timer triggered but {Remaining} remaining before deadline exceeded. Deadline timer rescheduled.")]
    public static partial void DeadlineTimerRescheduled(ILogger logger, TimeSpan remaining);

    [LoggerMessage(Level = LogLevel.Error, EventId = 26, EventName = "ErrorParsingTrailers", Message = "Error parsing trailers.")]
    public static partial void ErrorParsingTrailers(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, EventId = 27, EventName = "ErrorExceedingDeadline", Message = "Error exceeding deadline.")]
    public static partial void ErrorExceedingDeadline(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Error, EventId = 28, EventName = "InvalidGrpcStatusInHeader",
        Message = "Header contains an OK gRPC status. This is invalid for unary or client streaming calls because a status in the header indicates there is no response body." +
        " A message in the response body is required for unary and client streaming calls.")]
    public static partial void InvalidGrpcStatusInHeader(ILogger logger);

}
