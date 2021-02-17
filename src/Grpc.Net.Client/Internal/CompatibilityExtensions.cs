// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Internal
{
    internal static class CompatibilityExtensions
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
    }
}