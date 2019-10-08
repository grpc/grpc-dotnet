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

namespace Grpc.Net.Client.Internal
{
    internal static class GrpcCallLog
    {
        private static readonly Action<ILogger, MethodType, Uri, Exception?> _startingCall =
            LoggerMessage.Define<MethodType, Uri>(LogLevel.Debug, new EventId(1, "StartingCall"), "Starting gRPC call. Method type: '{MethodType}', URI: '{Uri}'.");

        private static readonly Action<ILogger, Exception?> _responseHeadersReceived =
            LoggerMessage.Define(LogLevel.Trace, new EventId(2, "ResponseHeadersReceived"), "Response headers received.");

        private static readonly Action<ILogger, StatusCode, string, Exception?> _grpcStatusError =
            LoggerMessage.Define<StatusCode, string>(LogLevel.Error, new EventId(3, "GrpcStatusError"), "Call failed with gRPC error status. Status code: '{StatusCode}', Message: '{StatusMessage}'.");

        private static readonly Action<ILogger, Exception?> _finishedCall =
            LoggerMessage.Define(LogLevel.Debug, new EventId(4, "FinishedCall"), "Finished gRPC call.");

        private static readonly Action<ILogger, TimeSpan, Exception?> _startingDeadlineTimeout =
            LoggerMessage.Define<TimeSpan>(LogLevel.Trace, new EventId(5, "StartingDeadlineTimeout"), "Starting deadline timeout. Duration: {DeadlineTimeout}.");

        private static readonly Action<ILogger, Exception?> _errorStartingCall =
            LoggerMessage.Define(LogLevel.Error, new EventId(6, "ErrorStartingCall"), "Error starting gRPC call.");

        private static readonly Action<ILogger, Exception?> _deadlineExceeded =
            LoggerMessage.Define(LogLevel.Warning, new EventId(7, "DeadlineExceeded"), "gRPC call deadline exceeded.");

        private static readonly Action<ILogger, Exception?> _canceledCall =
            LoggerMessage.Define(LogLevel.Debug, new EventId(8, "CanceledCall"), "gRPC call canceled.");

        private static readonly Action<ILogger, Exception?> _messageNotReturned =
            LoggerMessage.Define(LogLevel.Error, new EventId(9, "MessageNotReturned"), "Message not returned from unary or client streaming call.");

        private static readonly Action<ILogger, Exception?> _errorValidatingResponseHeaders =
            LoggerMessage.Define(LogLevel.Error, new EventId(10, "ErrorValidatingResponseHeaders"), "Error validating response headers.");

        private static readonly Action<ILogger, Exception?> _errorFetchingGrpcStatus =
            LoggerMessage.Define(LogLevel.Error, new EventId(11, "ErrorFetchingGrpcStatus"), "Error fetching gRPC status.");

        private static readonly Action<ILogger, Exception?> _callCredentialsNotUsed =
            LoggerMessage.Define(LogLevel.Warning, new EventId(12, "CallCredentialsNotUsed"), "The configured CallCredentials were not used because the call does not use TLS.");

        private static readonly Action<ILogger, Exception?> _readingMessage =
            LoggerMessage.Define(LogLevel.Debug, new EventId(13, "ReadingMessage"), "Reading message.");

        private static readonly Action<ILogger, Exception?> _noMessageReturned =
            LoggerMessage.Define(LogLevel.Trace, new EventId(14, "NoMessageReturned"), "No message returned.");

        private static readonly Action<ILogger, int, Type, Exception?> _deserializingMessage =
            LoggerMessage.Define<int, Type>(LogLevel.Trace, new EventId(15, "DeserializingMessage"), "Deserializing {MessageLength} byte message to '{MessageType}'.");

        private static readonly Action<ILogger, Exception?> _receivedMessage =
            LoggerMessage.Define(LogLevel.Trace, new EventId(16, "ReceivedMessage"), "Received message.");

        private static readonly Action<ILogger, Exception?> _errorReadingMessage =
            LoggerMessage.Define(LogLevel.Error, new EventId(17, "ErrorReadingMessage"), "Error reading message.");

        private static readonly Action<ILogger, Exception?> _sendingMessage =
            LoggerMessage.Define(LogLevel.Debug, new EventId(18, "SendingMessage"), "Sending message.");

        private static readonly Action<ILogger, Exception?> _messageSent =
            LoggerMessage.Define(LogLevel.Trace, new EventId(19, "MessageSent"), "Message sent.");

        private static readonly Action<ILogger, Exception?> _errorSendingMessage =
            LoggerMessage.Define(LogLevel.Error, new EventId(20, "ErrorSendingMessage"), "Error sending message.");

        private static readonly Action<ILogger, Type, int, Exception?> _serializedMessage =
            LoggerMessage.Define<Type, int>(LogLevel.Trace, new EventId(21, "SerializedMessage"), "Serialized '{MessageType}' to {MessageLength} byte message.");

        private static readonly Action<ILogger, string, Exception?> _compressingMessage =
            LoggerMessage.Define<string>(LogLevel.Trace, new EventId(22, "CompressingMessage"), "Compressing message with '{MessageEncoding}' encoding.");

        private static readonly Action<ILogger, string, Exception?> _decompressingMessage =
            LoggerMessage.Define<string>(LogLevel.Trace, new EventId(23, "DecompressingMessage"), "Decompressing message with '{MessageEncoding}' encoding.");

        public static void StartingCall(ILogger logger, MethodType methodType, Uri uri)
        {
            _startingCall(logger, methodType, uri, null);
        }

        public static void ResponseHeadersReceived(ILogger logger)
        {
            _responseHeadersReceived(logger, null);
        }

        public static void GrpcStatusError(ILogger logger, StatusCode status, string message)
        {
            _grpcStatusError(logger, status, message, null);
        }

        public static void FinishedCall(ILogger logger)
        {
            _finishedCall(logger, null);
        }

        public static void StartingDeadlineTimeout(ILogger logger, TimeSpan deadlineTimeout)
        {
            _startingDeadlineTimeout(logger, deadlineTimeout, null);
        }

        public static void ErrorStartingCall(ILogger logger, Exception ex)
        {
            _errorStartingCall(logger, ex);
        }

        public static void DeadlineExceeded(ILogger logger)
        {
            _deadlineExceeded(logger, null);
        }

        public static void CanceledCall(ILogger logger)
        {
            _canceledCall(logger, null);
        }

        public static void MessageNotReturned(ILogger logger)
        {
            _messageNotReturned(logger, null);
        }

        public static void ErrorValidatingResponseHeaders(ILogger logger, Exception ex)
        {
            _errorValidatingResponseHeaders(logger, ex);
        }

        public static void ErrorFetchingGrpcStatus(ILogger logger, Exception ex)
        {
            _errorFetchingGrpcStatus(logger, ex);
        }

        public static void CallCredentialsNotUsed(ILogger logger)
        {
            _callCredentialsNotUsed(logger, null);
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
