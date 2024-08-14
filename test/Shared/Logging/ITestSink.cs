// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System;
using System.Collections.Concurrent;

// Copied with permission from https://github.com/dotnet/aspnetcore/tree/08b60af1bca8cffff8ba0a72164fb7505ffe114d/src/Testing/src/Logging
namespace Microsoft.Extensions.Logging.Testing;

public interface ITestSink
{
    event Action<WriteContext> MessageLogged;

    event Action<BeginScopeContext> ScopeStarted;

    Func<WriteContext, bool> WriteEnabled { get; set; }

    Func<BeginScopeContext, bool> BeginEnabled { get; set; }

    IProducerConsumerCollection<BeginScopeContext> Scopes { get; set; }

    IProducerConsumerCollection<WriteContext> Writes { get; set; }

    void Write(WriteContext context);

    void Begin(BeginScopeContext context);
}
