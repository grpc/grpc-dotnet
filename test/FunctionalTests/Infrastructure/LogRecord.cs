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

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class LogRecord
    {
        public LogRecord(DateTime timestamp, LogLevel logLevel, EventId eventId, object state, Exception? exception, Func<object, Exception?, string> formatter, string loggerName)
        {
            Timestamp = timestamp;
            LogLevel = logLevel;
            EventId = eventId;
            State = state;
            Exception = exception;
            Formatter = formatter;
            LoggerName = loggerName;
        }

        public DateTime Timestamp { get; }

        public LogLevel LogLevel { get; }

        public EventId EventId { get; }

        public object State { get; }

        public Exception? Exception { get; }

        public Func<object, Exception?, string> Formatter { get; }

        public string LoggerName { get; }

        public string Message => Formatter(State, Exception) ?? string.Empty;
    }
}
