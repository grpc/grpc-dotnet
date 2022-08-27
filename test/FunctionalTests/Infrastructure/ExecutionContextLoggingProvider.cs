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

using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class ExecutionContextLoggingProvider : ILoggerProvider
    {
        private readonly TextWriter _writer;
        private readonly ExecutionContext _executionContext;
        private readonly DateTimeOffset _timeStart;

        public ExecutionContextLoggingProvider(TextWriter writer, ExecutionContext executionContext)
        {
            _writer = writer;
            _executionContext = executionContext;
            _timeStart = DateTimeOffset.UtcNow;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new ExecutionContextLogger(_timeStart, _writer, _executionContext, categoryName);
        }

        public void Dispose()
        {
        }

        private class ExecutionContextLogger : ILogger
        {
            private readonly DateTimeOffset _timeStart;
            private readonly TextWriter _writer;
            private readonly ExecutionContext _executionContext;
            private readonly string _categoryName;

            public ExecutionContextLogger(DateTimeOffset timeStart, TextWriter writer, ExecutionContext executionContext, string categoryName)
            {
                _timeStart = timeStart;
                _writer = writer;
                _executionContext = executionContext;
                _categoryName = categoryName;
            }

#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
            public IDisposable BeginScope<TState>(TState state)
            {
                return null!;
            }
#pragma warning restore CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                // Log using the passed in execution context.
                // In the case of NUnit, console output is only captured by the test
                // if it is written in the test's execution context.
                ExecutionContext.Run(_executionContext, s =>
                {
                    var timestamp = $"{(DateTimeOffset.UtcNow - _timeStart).TotalSeconds:N3}s";

                    var logLine = timestamp + " " + _categoryName + " - " + logLevel + ": " + formatter(state, exception);

                    _writer.WriteLine(logLine);
                    if (exception != null)
                    {
                        _writer.WriteLine(exception);
                    }
                }, null);
            }
        }
    }
}
