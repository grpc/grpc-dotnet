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
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class GrpcServerLog
    {
        private static readonly Action<ILogger, Exception?> _unableToDisableMaxRequestBodySize =
            LoggerMessage.Define(LogLevel.Debug, new EventId(1, "UnableToDisableMaxRequestBodySizeLimit"), "Unable to disable the max request body size limit.");

        private static readonly Action<ILogger, string?, Exception?> _unsupportedRequestContentType =
            LoggerMessage.Define<string?>(LogLevel.Information, new EventId(2, "UnsupportedRequestContentType"), "Request content-type of '{ContentType}' is not supported.");

        private static readonly Action<ILogger, string?, Exception?> _unsupportedRequestProtocol =
            LoggerMessage.Define<string?>(LogLevel.Information, new EventId(3, "UnsupportedRequestProtocol"), "Request protocol of '{Protocol}' is not supported.");

        private static readonly Action<ILogger, TimeSpan, Exception?> _deadlineExceeded =
            LoggerMessage.Define<TimeSpan>(LogLevel.Debug, new EventId(4, "DeadlineExceeded"), "Request with timeout of {Timeout} has exceeded its deadline.");

        private static readonly Action<ILogger, string, Exception?> _invalidTimeoutIgnored =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, "InvalidTimeoutIgnored"), "Invalid grpc-timeout header value '{Timeout}' has been ignored.");

        private static readonly Action<ILogger, string, Exception?> _errorExecutingServiceMethod =
            LoggerMessage.Define<string>(LogLevel.Error, new EventId(6, "ErrorExecutingServiceMethod"), "Error when executing service method '{ServiceMethod}'.");

        private static readonly Action<ILogger, StatusCode, Exception?> _rpcConnectionError =
            LoggerMessage.Define<StatusCode>(LogLevel.Information, new EventId(7, "RpcConnectionError"), "Error status code '{StatusCode}' raised.");

        private static readonly Action<ILogger, string, Exception?> _encodingNotInAcceptEncoding =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(8, "EncodingNotInAcceptEncoding"), "Request grpc-encoding header value '{GrpcEncoding}' is not in grpc-accept-encoding.");

        private static readonly Action<ILogger, Exception?> _deadlineCancellationError =
            LoggerMessage.Define(LogLevel.Error, new EventId(9, "DeadlineCancellationError"), "Error occurred while trying to cancel the request due to deadline exceeded.");

        private static readonly Action<ILogger, Exception?> _readingMessage =
            LoggerMessage.Define(LogLevel.Debug, new EventId(10, "ReadingMessage"), "Reading message.");

        private static readonly Action<ILogger, Exception?> _noMessageReturned =
            LoggerMessage.Define(LogLevel.Trace, new EventId(11, "NoMessageReturned"), "No message returned.");

        private static readonly Action<ILogger, int, Type, Exception?> _deserializingMessage =
            LoggerMessage.Define<int, Type>(LogLevel.Trace, new EventId(12, "DeserializingMessage"), "Deserializing {MessageLength} byte message to '{MessageType}'.");

        private static readonly Action<ILogger, Exception?> _receivedMessage =
            LoggerMessage.Define(LogLevel.Trace, new EventId(13, "ReceivedMessage"), "Received message.");

        private static readonly Action<ILogger, Exception?> _errorReadingMessage =
            LoggerMessage.Define(LogLevel.Error, new EventId(14, "ErrorReadingMessage"), "Error reading message.");

        private static readonly Action<ILogger, Exception?> _sendingMessage =
            LoggerMessage.Define(LogLevel.Debug, new EventId(15, "SendingMessage"), "Sending message.");

        private static readonly Action<ILogger, Exception?> _messageSent =
            LoggerMessage.Define(LogLevel.Trace, new EventId(16, "MessageSent"), "Message sent.");

        private static readonly Action<ILogger, Exception?> _errorSendingMessage =
            LoggerMessage.Define(LogLevel.Error, new EventId(17, "ErrorSendingMessage"), "Error sending message.");

        private static readonly Action<ILogger, Type, int, Exception?> _serializedMessage =
            LoggerMessage.Define<Type, int>(LogLevel.Trace, new EventId(18, "SerializedMessage"), "Serialized '{MessageType}' to {MessageLength} byte message.");

        private static readonly Action<ILogger, string, Exception?> _compressingMessage =
            LoggerMessage.Define<string>(LogLevel.Trace, new EventId(19, "CompressingMessage"), "Compressing message with '{MessageEncoding}' encoding.");

        private static readonly Action<ILogger, string, Exception?> _decompressingMessage =
            LoggerMessage.Define<string>(LogLevel.Trace, new EventId(20, "DecompressingMessage"), "Decompressing message with '{MessageEncoding}' encoding.");

        public static void DeadlineExceeded(ILogger logger, TimeSpan timeout)
        {
            _deadlineExceeded(logger, timeout, null);
        }

        public static void InvalidTimeoutIgnored(ILogger logger, string timeout)
        {
            _invalidTimeoutIgnored(logger, timeout, null);
        }

        public static void ErrorExecutingServiceMethod(ILogger logger, string serviceMethod, Exception ex)
        {
            _errorExecutingServiceMethod(logger, serviceMethod, ex);
        }

        public static void RpcConnectionError(ILogger logger, StatusCode statusCode, Exception ex)
        {
            _rpcConnectionError(logger, statusCode, ex);
        }

        public static void EncodingNotInAcceptEncoding(ILogger logger, string grpcEncoding)
        {
            _encodingNotInAcceptEncoding(logger, grpcEncoding, null);
        }

        public static void DeadlineCancellationError(ILogger logger, Exception ex)
        {
            _deadlineCancellationError(logger, ex);
        }

        public static void UnableToDisableMaxRequestBodySize(ILogger logger)
        {
            _unableToDisableMaxRequestBodySize(logger, null);
        }

        public static void UnsupportedRequestContentType(ILogger logger, string? contentType)
        {
            _unsupportedRequestContentType(logger, contentType, null);
        }

        public static void UnsupportedRequestProtocol(ILogger logger, string? protocol)
        {
            _unsupportedRequestProtocol(logger, protocol, null);
        }

        public static void ReadingMessage(ILogger logger)
        {
            _readingMessage(logger, null);
        }

        public static void NoMessageReturned(ILogger logger)
        {
            _noMessageReturned(logger, null);
        }

        public static void DeserializingMessage(ILogger logger, int messageLength, Type messageType)
        {
            _deserializingMessage(logger, messageLength, messageType, null);
        }

        public static void ReceivedMessage(ILogger logger)
        {
            _receivedMessage(logger, null);
        }

        public static void ErrorReadingMessage(ILogger logger, Exception ex)
        {
            _errorReadingMessage(logger, ex);
        }

        public static void SendingMessage(ILogger logger)
        {
            _sendingMessage(logger, null);
        }

        public static void MessageSent(ILogger logger)
        {
            _messageSent(logger, null);
        }

        public static void ErrorSendingMessage(ILogger logger, Exception ex)
        {
            _errorSendingMessage(logger, ex);
        }

        public static void SerializedMessage(ILogger logger, Type messageType, int messageLength)
        {
            _serializedMessage(logger, messageType, messageLength, null);
        }

        public static void CompressingMessage(ILogger logger, string messageEncoding)
        {
            _compressingMessage(logger, messageEncoding, null);
        }

        public static void DecompressingMessage(ILogger logger, string messageEncoding)
        {
            _decompressingMessage(logger, messageEncoding, null);
        }
    }
}
