// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Grpc.Shared
{
    internal static class CompatibilityHelpers
    {
        [Conditional("DEBUG")]
        public static void Assert([DoesNotReturnIf(false)] bool condition, string? message = null)
        {
            Debug.Assert(condition, message);
        }

        public static bool IsCompletedSuccessfully(this Task task)
        {
            // IsCompletedSuccessfully is the faster method, but only currently exposed on .NET Core 2.0+
#if !NETSTANDARD2_0
            return task.IsCompletedSuccessfully;
#else
            return task.Status == TaskStatus.RanToCompletion;
#endif
        }

#if !NETSTANDARD2_0
        public static bool IsCompletedSuccessfully(this ValueTask task)
        {
            return task.IsCompletedSuccessfully;
        }

        public static bool IsCompletedSuccessfully<T>(this ValueTask<T> task)
        {
            return task.IsCompletedSuccessfully;
        }
#endif

        public static int IndexOf(string s, char value, StringComparison comparisonType)
        {
#if NETSTANDARD2_0
            return s.IndexOf(value);
#else
            return s.IndexOf(value, comparisonType);
#endif
        }
    }
}