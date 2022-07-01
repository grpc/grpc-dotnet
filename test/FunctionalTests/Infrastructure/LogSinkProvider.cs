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

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class LogSinkProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly ConcurrentQueue<LogRecord> _logs = new ConcurrentQueue<LogRecord>();

        private IExternalScopeProvider? _scopeProvider;

        public event Action<LogRecord>? RecordLogged;

        public ILogger CreateLogger(string categoryName)
        {
            return new LogSinkLogger(categoryName, this, _scopeProvider);
        }

        public void Dispose()
        {
        }

        public IList<LogRecord> GetLogs() => _logs.ToList();

        public void ClearLogs() => _logs.Clear();

        public void Log<TState>(string categoryName, LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var record = new LogRecord(DateTime.Now, logLevel, eventId, state!, exception, (o, e) => formatter((TState)o, e), categoryName);
            _logs.Enqueue(record);

            RecordLogged?.Invoke(record);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        private class LogSinkLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly LogSinkProvider _logSinkProvider;
            private readonly IExternalScopeProvider? _scopeProvider;

            public LogSinkLogger(string categoryName, LogSinkProvider logSinkProvider, IExternalScopeProvider? scopeProvider)
            {
                _categoryName = categoryName;
                _logSinkProvider = logSinkProvider;
                _scopeProvider = scopeProvider;
            }

#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
            public IDisposable BeginScope<TState>(TState state)
            {
                return _scopeProvider != null ? _scopeProvider.Push(state) : NullScope.Instance;
            }
#pragma warning restore CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                _logSinkProvider.Log(_categoryName, logLevel, eventId, state, exception, formatter);
            }
        }

        private class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
