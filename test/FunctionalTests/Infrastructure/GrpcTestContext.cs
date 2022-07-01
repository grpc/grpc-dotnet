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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public sealed class GrpcTestContext : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ConcurrentDictionary<string, ILogger> _serverLoggers;
        private readonly object _lock = new object();

        public ILoggerFactory LoggerFactory { get; }
        public ILogger Logger { get; }

        public VerifyNoErrorsScope Scope { get; }

        public GrpcTestContext()
        {
            _serverLoggers = new ConcurrentDictionary<string, ILogger>(StringComparer.Ordinal);

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Trace);
                builder.AddFilter((s, l) =>
                {
                    // Ensure not null
                    s ??= string.Empty;

                    // Server categories are prefixed with SERVER
                    var category = s.StartsWith("SERVER ") ? s.AsSpan(7) : s.AsSpan();

                    if (category.SequenceEqual("Microsoft.AspNetCore.Routing.Matching.DfaMatcher"))
                    {
                        // Routing matcher is quite verbose at Debug level
                        // Only capture Info and above
                        return l >= LogLevel.Information;
                    }
                    else if (category.StartsWith("Microsoft") || category.StartsWith("System"))
                    {
                        return l >= LogLevel.Trace;
                    }

                    return true;
                });

                // Provider needs to be created with with the test's execution context
                // or else console logging is not associated with the test
                builder.AddProvider(new ExecutionContextLoggingProvider(Console.Out, ExecutionContext.Capture()!));
            });
            _serviceProvider = services.BuildServiceProvider(validateScopes: true);
            LoggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
            Logger = LoggerFactory.CreateLogger(nameof(GrpcTestContext));
            Scope = new VerifyNoErrorsScope(LoggerFactory, wrappedDisposable: null, expectedErrorsFilter: null);

            Logger.LogInformation($"Starting {GetTestName()}");
        }

        public void ServerFixtureOnServerLogged(LogRecord logRecord)
        {
            if (logRecord == null)
            {
                return;
            }

            ILogger logger;

            lock (_lock)
            {
                // Create (or get) a logger with the same name as the server logger
                // Call in the lock to avoid ODE where LoggerFactory could be disposed by the wrapped disposable
                logger = _serverLoggers.GetOrAdd(logRecord.LoggerName, loggerName => LoggerFactory.CreateLogger("SERVER " + loggerName));
            }

            logger.Log(logRecord.LogLevel, logRecord.EventId, logRecord.State, logRecord.Exception, logRecord.Formatter);
        }

        public void Dispose()
        {
            Logger.LogInformation($"Finishing {GetTestName()}");

            _serviceProvider.Dispose();

            // This will verify only expected errors were logged on the server for the previous test.
            Scope.Dispose();
        }

        private string GetTestName()
        {
            var className = TestContext.CurrentContext.Test.ClassName!;
            var periodIndex = className.LastIndexOf('.');
            if (periodIndex > 0)
            {
                className = className.Substring(periodIndex + 1);
            }

            return className + "." + TestContext.CurrentContext.Test.Name;
        }
    }
}
