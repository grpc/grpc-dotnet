// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;

// Copied with permission from https://github.com/dotnet/aspnetcore/tree/08b60af1bca8cffff8ba0a72164fb7505ffe114d/src/Testing/src/Logging
namespace Microsoft.Extensions.Logging.Testing;

public class TestLogger<T> : ILogger<T>
{
    private readonly ILogger<T> _logger;

    public TestLogger(TestLoggerFactory factory)
    {
        _logger = factory.CreateLogger<T>();
    }

    public IDisposable BeginScope<TState>(TState state)
    {
        return _logger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _logger.IsEnabled(logLevel);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception exception,
        Func<TState, Exception, string> formatter)
    {
        _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
