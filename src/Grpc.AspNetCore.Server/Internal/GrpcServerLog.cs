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

namespace Grpc.AspNetCore.Server.Internal;

internal static partial class GrpcServerLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "UnableToDisableMaxRequestBodySizeLimit", Message = "Unable to disable the max request body size limit.")]
    public static partial void UnableToDisableMaxRequestBodySize(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 2, EventName = "UnsupportedRequestContentType", Message = "Request content-type of '{ContentType}' is not supported.")]
    public static partial void UnsupportedRequestContentType(ILogger logger, string? contentType);

    [LoggerMessage(Level = LogLevel.Information, EventId = 3, EventName = "UnsupportedRequestProtocol", Message = "Request protocol of '{Protocol}' is not supported.")]
    public static partial void UnsupportedRequestProtocol(ILogger logger, string? protocol);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 4, EventName = "DeadlineExceeded", Message = "Request with timeout of {Timeout} has exceeded its deadline.")]
    public static partial void DeadlineExceeded(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 5, EventName = "InvalidTimeoutIgnored", Message = "Invalid grpc-timeout header value '{Timeout}' has been ignored.")]
    public static partial void InvalidTimeoutIgnored(ILogger logger, string timeout);

    [LoggerMessage(Level = LogLevel.Error, EventId = 6, EventName = "ErrorExecutingServiceMethod", Message = "Error when executing service method '{ServiceMethod}'.")]
    public static partial void ErrorExecutingServiceMethod(ILogger logger, string serviceMethod, Exception ex);

    [LoggerMessage(Level = LogLevel.Information, EventId = 7, EventName = "RpcConnectionError", Message = "Error status code '{StatusCode}' with detail '{Detail}' raised.")]
    public static partial void RpcConnectionError(ILogger logger, StatusCode statusCode, string detail, Exception? debugException);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 8, EventName = "EncodingNotInAcceptEncoding", Message = "Request grpc-encoding header value '{GrpcEncoding}' is not in grpc-accept-encoding.")]
    public static partial void EncodingNotInAcceptEncoding(ILogger logger, string grpcEncoding);

    [LoggerMessage(Level = LogLevel.Error, EventId = 9, EventName = "DeadlineCancellationError", Message = "Error occurred while trying to cancel the request due to deadline exceeded.")]
    public static partial void DeadlineCancellationError(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 10, EventName = "ReadingMessage", Message = "Reading message.")]
    public static partial void ReadingMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 11, EventName = "NoMessageReturned", Message = "No message returned.")]
    public static partial void NoMessageReturned(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 12, EventName = "DeserializingMessage", Message = "Deserializing {MessageLength} byte message to '{MessageType}'.")]
    public static partial void DeserializingMessage(ILogger logger, int messageLength, Type messageType);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 13, EventName = "ReceivedMessage", Message = "Received message.")]
    public static partial void ReceivedMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 14, EventName = "ErrorReadingMessage", Message = "Error reading message.")]
    public static partial void ErrorReadingMessage(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 15, EventName = "SendingMessage", Message = "Sending message.")]
    public static partial void SendingMessage(ILogger logger);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 16, EventName = "MessageSent", Message = "Message sent.")]
    public static partial void MessageSent(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 17, EventName = "ErrorSendingMessage", Message = "Error sending message.")]
    public static partial void ErrorSendingMessage(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 18, EventName = "SerializedMessage", Message = "Serialized '{MessageType}' to {MessageLength} byte message.")]
    public static partial void SerializedMessage(ILogger logger, Type messageType, int messageLength);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 19, EventName = "CompressingMessage", Message = "Compressing message with '{MessageEncoding}' encoding.")]
    public static partial void CompressingMessage(ILogger logger, string messageEncoding);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 20, EventName = "DecompressingMessage", Message = "Decompressing message with '{MessageEncoding}' encoding.")]
    public static partial void DecompressingMessage(ILogger logger, string messageEncoding);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 21, EventName = "ResettingResponse", Message = "Resetting response stream with error code {ErrorCode}.")]
    public static partial void ResettingResponse(ILogger logger, int errorCode);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 22, EventName = "AbortingResponse", Message = "IHttpResetFeature is not available so unable to cleanly reset response stream. Aborting response stream.")]
    public static partial void AbortingResponse(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 23, EventName = "UnhandledCorsPreflightRequest", Message = "Unhandled CORS preflight request received. CORS may not be configured correctly in the application.")]
    public static partial void UnhandledCorsPreflightRequest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 24, EventName = "DeadlineTimeoutTooLong", Message = "Deadline timeout {Timeout} is above maximum allowed timeout of 99999999 seconds. Maximum timeout will be used.")]
    public static partial void DeadlineTimeoutTooLong(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 25, EventName = "DeadlineTimerRescheduled", Message = "Deadline timer triggered but {Remaining} remaining before deadline exceeded. Deadline timer rescheduled.")]
    public static partial void DeadlineTimerRescheduled(ILogger logger, TimeSpan remaining);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 26, EventName = "DeadlineStarted", Message = "Request deadline timeout of {Timeout} started.")]
    public static partial void DeadlineStarted(ILogger logger, TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Trace, EventId = 27, EventName = "DeadlineStopped", Message = "Request deadline stopped.")]
    internal static partial void DeadlineStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, EventId = 28, EventName = "ServiceMethodCanceled", Message = "Service method '{ServiceMethod}' canceled.")]
    public static partial void ServiceMethodCanceled(ILogger logger, string serviceMethod, Exception ex);
}
