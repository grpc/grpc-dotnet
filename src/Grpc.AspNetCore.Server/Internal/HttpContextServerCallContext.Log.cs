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
    internal sealed partial class HttpContextServerCallContext
    {
        private static class Log
        {
            private static readonly Action<ILogger, TimeSpan, Exception?> _deadlineExceeded =
                LoggerMessage.Define<TimeSpan>(LogLevel.Debug, new EventId(1, "DeadlineExceeded"), "Request with timeout of {Timeout} has exceeded its deadline.");

            private static readonly Action<ILogger, string, Exception?> _invalidTimeoutIgnored =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, "InvalidTimeoutIgnored"), "Invalid grpc-timeout header value '{Timeout}' has been ignored.");

            private static readonly Action<ILogger, string, Exception?> _errorExecutingServiceMethod =
                LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "ErrorExecutingServiceMethod"), "Error when executing service method '{ServiceMethod}'.");

            private static readonly Action<ILogger, StatusCode, Exception?> _rpcConnectionError =
                LoggerMessage.Define<StatusCode>(LogLevel.Information, new EventId(4, "RpcConnectionError"), "Error status code '{StatusCode}' raised.");

            private static readonly Action<ILogger, string, Exception?> _encodingNotInAcceptEncoding =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(5, "EncodingNotInAcceptEncoding"), "Request grpc-encoding header value '{GrpcEncoding}' is not in grpc-accept-encoding.");

            private static readonly Action<ILogger, Exception?> _deadlineCancellationError =
                LoggerMessage.Define(LogLevel.Error, new EventId(6, "DeadlineCancellationError"), "Error occurred while trying to cancel the request due to deadline exceeded.");

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
        }
    }
}
