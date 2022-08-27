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

namespace InteropTestsGrpcWebClient.Infrastructure
{
    public class PageLoggerFactory : ILoggerFactory
    {
        private readonly Action<LogLevel, string> _writeAction;

        public PageLoggerFactory(Action<LogLevel, string> writeAction)
        {
            _writeAction = writeAction;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new PageLogger(categoryName, _writeAction);
        }

        public void Dispose()
        {
        }

        private class PageLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly Action<LogLevel, string> _writeAction;

            public PageLogger(string categoryName, Action<LogLevel, string> writeAction)
            {
                _categoryName = categoryName;
                _writeAction = writeAction;
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
                var message = $"{logLevel.ToString().ToUpper()} {formatter(state, exception)}";

                Console.WriteLine(message);
                _writeAction(logLevel, message);
            }
        }
    }
}
