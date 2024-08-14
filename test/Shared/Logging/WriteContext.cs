// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;

// Copied with permission from https://github.com/dotnet/aspnetcore/tree/08b60af1bca8cffff8ba0a72164fb7505ffe114d/src/Testing/src/Logging
namespace Microsoft.Extensions.Logging.Testing;

public class WriteContext
{
    public LogLevel LogLevel { get; set; }

    public EventId EventId { get; set; }

    public object State { get; set; }

    public Exception Exception { get; set; }

    public Func<object, Exception, string> Formatter { get; set; }

    public object Scope { get; set; }

    public string LoggerName { get; set; }

    public string Message
    {
        get
        {
            return Formatter(State, Exception);
        }
    }

    public override string ToString()
    {
        return $"{LogLevel} {LoggerName}: {Message}";
    }
}
