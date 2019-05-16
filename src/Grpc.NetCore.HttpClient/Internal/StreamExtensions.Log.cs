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
using Microsoft.Extensions.Logging;

namespace Grpc.NetCore.HttpClient
{
    internal static partial class StreamExtensions
    {
        private static class Log
        {
            private static readonly Action<ILogger, Exception?> _readingMessage =
                LoggerMessage.Define(LogLevel.Debug, new EventId(1, "ReadingMessage"), "Reading message.");

            private static readonly Action<ILogger, Exception?> _noMessageReturned =
                LoggerMessage.Define(LogLevel.Trace, new EventId(2, "NoMessageReturned"), "No message returned.");

            private static readonly Action<ILogger, int, Type, Exception?> _deserializingMessage =
                LoggerMessage.Define<int, Type>(LogLevel.Trace, new EventId(3, "DeserializingMessage"), "Deserializing {MessageLength} byte message to '{MessageType}'.");

            private static readonly Action<ILogger, Exception?> _receivedMessage =
                LoggerMessage.Define(LogLevel.Trace, new EventId(4, "ReceivedMessage"), "Received message.");

            private static readonly Action<ILogger, Exception?> _errorReadingMessage =
                LoggerMessage.Define(LogLevel.Error, new EventId(5, "ErrorReadingMessage"), "Error reading message.");

            private static readonly Action<ILogger, Exception?> _sendingMessage =
                LoggerMessage.Define(LogLevel.Debug, new EventId(6, "SendingMessage"), "Sending message.");

            private static readonly Action<ILogger, Exception?> _messageSent =
                LoggerMessage.Define(LogLevel.Trace, new EventId(7, "MessageSent"), "Message sent.");

            private static readonly Action<ILogger, Exception?> _errorSendingMessage =
                LoggerMessage.Define(LogLevel.Error, new EventId(8, "ErrorSendingMessage"), "Error reading message.");

            private static readonly Action<ILogger, Type, int, Exception?> _serializedMessage =
                LoggerMessage.Define<Type, int>(LogLevel.Trace, new EventId(9, "SerializedMessage"), "Serialized '{MessageType}' to {MessageLength} byte message.");

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
        }
    }
}
