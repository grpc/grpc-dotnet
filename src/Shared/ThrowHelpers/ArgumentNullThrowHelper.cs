// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Grpc.Shared;

internal static partial class ArgumentNullThrowHelper
{
    /// <summary>Throws an <see cref="ArgumentNullException"/> if <paramref name="argument"/> is null.</summary>
    /// <param name="argument">The reference type argument to validate as non-null.</param>
    /// <param name="paramName">The name of the parameter with which <paramref name="argument"/> corresponds.</param>
    public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
#if !NET6_0_OR_GREATER
        if (argument is null)
        {
            Throw(paramName);
        }
#else
        ArgumentNullException.ThrowIfNull(argument, paramName);
#endif
    }

#if !NET6_0_OR_GREATER
    [DoesNotReturn]
    internal static void Throw(string? paramName) =>
        throw new ArgumentNullException(paramName);
#endif
}
