// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;

// Copied with permission from https://github.com/dotnet/aspnetcore/tree/08b60af1bca8cffff8ba0a72164fb7505ffe114d/src/Testing/src/Logging
namespace Microsoft.Extensions.Logging.Testing;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false)]
public class LogLevelAttribute : Attribute
{
    public LogLevelAttribute(LogLevel logLevel)
    {
        LogLevel = logLevel;
    }

    public LogLevel LogLevel { get; }
}
