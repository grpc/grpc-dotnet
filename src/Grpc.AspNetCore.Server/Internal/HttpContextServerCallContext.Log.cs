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
using System.Globalization;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Internal
{
    internal sealed partial class HttpContextServerCallContext
    {
        private static readonly Action<ILogger, TimeSpan, Exception> _deadlineExceeded =
            LoggerMessage.Define<TimeSpan>(LogLevel.Debug, new EventId(1, "DeadlineExceeded"), "Request with timeout of {Timeout} has exceeded its deadline.");

        private static readonly Action<ILogger, string, Exception> _invalidTimeoutIgnored =
            LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, "InvalidTimeoutIgnored"), "Invalid grpc-timeout header value '{Timeout}' has been ignored.");

        public static void DeadlineExceeded(ILogger logger, TimeSpan timeout)
        {
            _deadlineExceeded(logger, timeout, null);
        }

        public static void InvalidTimeoutIgnored(ILogger logger, string timeout)
        {
            _invalidTimeoutIgnored(logger, timeout, null);
        }
    }
}
