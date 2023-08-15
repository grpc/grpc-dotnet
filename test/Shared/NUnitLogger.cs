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

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Grpc.Tests.Shared;

internal static class NUnitLoggerExtensions
{
    public static void AddNUnitLogger(this IServiceCollection services)
    {
        services.AddLogging(b =>
        {
            b.AddProvider(new NUnitLoggerProvider());
            b.SetMinimumLevel(LogLevel.Trace);
        });
    }
}

internal class NUnitLoggerProvider : ILoggerProvider
{
    private readonly Stopwatch _stopwatch;
    private readonly ExecutionContext _executionContext;

    public NUnitLoggerProvider()
    {
        _stopwatch = Stopwatch.StartNew();
        _executionContext = ExecutionContext.Capture()!;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new NUnitLogger(categoryName, _stopwatch, _executionContext);
    }

    public void Dispose()
    {
    }
}

internal class NUnitLogger : ILogger, IDisposable
{
    private readonly Action<string> _output = Console.WriteLine;
    private readonly string _categoryName;
    private readonly Stopwatch _stopwatch;
    private readonly ExecutionContext _executionContext;

    public NUnitLogger(string categoryName, Stopwatch stopwatch, ExecutionContext executionContext)
    {
        _categoryName = categoryName;
        _stopwatch = stopwatch;
        _executionContext = executionContext;
    }

    public void Dispose()
    {
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
#if NET462
        // .NET Framework was throwing when ExecutionContext.Run was called:
        // Cannot apply a context that has been marshaled across AppDomains, that was not acquired
        // through a Capture operation or that has already been the argument to a Set call.

        Write(logLevel, state, exception, formatter);
#else
        // Log using the passed in execution context.
        // In the case of NUnit, console output is only captured by the test
        // if it is written in the test's execution context.
        ExecutionContext.Run(_executionContext, s =>
        {
            Write(logLevel, state, exception, formatter);
        }, null);
#endif
    }

    private void Write<TState>(LogLevel logLevel, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var timestamp = $"{_stopwatch.Elapsed.TotalSeconds:N3}s";

        var logLine = timestamp + " " + _categoryName + " - " + logLevel + ": " + formatter(state, exception);

        _output(logLine);
        if (exception != null)
        {
            _output(exception.ToString());
        }
    }

    public bool IsEnabled(LogLevel logLevel) => true;

#pragma warning disable CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
    public IDisposable BeginScope<TState>(TState state)
    {
        return this;
    }
#pragma warning restore CS8633 // Nullability in constraints for type parameter doesn't match the constraints for type parameter in implicitly implemented interface method'.
}
