// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

// Copied with permission from https://github.com/dotnet/aspnetcore/tree/08b60af1bca8cffff8ba0a72164fb7505ffe114d/src/Testing/src/Logging
namespace Microsoft.Extensions.Logging.Testing;

public class TestLoggerFactory : ILoggerFactory
{
    private readonly ITestSink _sink;
    private readonly bool _enabled;

    public TestLoggerFactory(ITestSink sink, bool enabled)
    {
        _sink = sink;
        _enabled = enabled;
    }

    public ILogger CreateLogger(string name)
    {
        return new TestLogger(name, _sink, _enabled);
    }

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }
}
