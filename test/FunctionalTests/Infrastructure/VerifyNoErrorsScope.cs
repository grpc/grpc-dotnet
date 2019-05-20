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
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    /// <summary>
    /// Attaches a log sink to a logger factory and captures all log messages.
    /// Disposing the scope will verify only expected errors were logged.
    /// </summary>
    public class VerifyNoErrorsScope : IDisposable
    {
        private readonly IDisposable? _wrappedDisposable;
        private readonly LogSinkProvider _sink;

        public Func<LogRecord, bool>? ExpectedErrorsFilter { get; set; }
        public ILoggerFactory LoggerFactory { get; }

        public VerifyNoErrorsScope(ILoggerFactory? loggerFactory = null, IDisposable? wrappedDisposable = null, Func<LogRecord, bool>? expectedErrorsFilter = null)
        {
            _wrappedDisposable = wrappedDisposable;
            ExpectedErrorsFilter = expectedErrorsFilter;
            _sink = new LogSinkProvider();

            LoggerFactory = loggerFactory ?? new LoggerFactory();
            LoggerFactory.AddProvider(_sink);
        }

        public void Dispose()
        {
            _wrappedDisposable?.Dispose();

            var results = _sink.GetLogs().Where(w => w.LogLevel >= LogLevel.Error || w.EventId.Name == "RpcConnectionError").ToList();

            if (ExpectedErrorsFilter != null)
            {
                results = results.Where(w => !ExpectedErrorsFilter(w)).ToList();
            }

            if (results.Count > 0)
            {
                string errorMessage = $"{results.Count} error(s) logged.";
                errorMessage += Environment.NewLine;
                errorMessage += string.Join(Environment.NewLine, results.Select(record =>
                {
                    string lineMessage = record.LoggerName + " - " + record.EventId.ToString() + " - " + record.Formatter(record.State, record.Exception);
                    if (record.Exception != null)
                    {
                        lineMessage += Environment.NewLine;
                        lineMessage += "===================";
                        lineMessage += Environment.NewLine;
                        lineMessage += record.Exception;
                        lineMessage += Environment.NewLine;
                        lineMessage += "===================";
                    }
                    return lineMessage;
                }));

                throw new Exception(errorMessage);
            }
        }
    }
}
