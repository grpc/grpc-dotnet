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
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FunctionalTestsWebsite.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class VerifyNoErrorsScope : IDisposable
    {
        private readonly IDisposable _wrappedDisposable;
        private readonly LogSinkProvider _sink;

        public Func<WriteContext, bool> ExpectedErrorsFilter { get; set; }
        public ILoggerFactory LoggerFactory { get; }

        public VerifyNoErrorsScope(ILoggerFactory loggerFactory = null, IDisposable wrappedDisposable = null, Func<WriteContext, bool> expectedErrorsFilter = null)
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

            var results = _sink.GetLogs().Where(w => w.Write.LogLevel >= LogLevel.Error || w.Write.EventId.Name == "RpcConnectionError").ToList();

            if (ExpectedErrorsFilter != null)
            {
                results = results.Where(w => !ExpectedErrorsFilter(w.Write)).ToList();
            }

            if (results.Count > 0)
            {
                string errorMessage = $"{results.Count} error(s) logged.";
                errorMessage += Environment.NewLine;
                errorMessage += string.Join(Environment.NewLine, results.Select(record =>
                {
                    var r = record.Write;

                    string lineMessage = r.LoggerName + " - " + r.EventId.ToString() + " - " + r.Formatter(r.State, r.Exception);
                    if (r.Exception != null)
                    {
                        lineMessage += Environment.NewLine;
                        lineMessage += "===================";
                        lineMessage += Environment.NewLine;
                        lineMessage += r.Exception;
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
