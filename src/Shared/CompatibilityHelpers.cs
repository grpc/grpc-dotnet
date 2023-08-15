// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Grpc.Shared;

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
#if !NETSTANDARD2_0 && !NET462
        return task.IsCompletedSuccessfully;
#else
        return task.Status == TaskStatus.RanToCompletion;
#endif
    }

#if !NETSTANDARD2_0 && !NET462
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
#if NETSTANDARD2_0 || NET462
        return s.IndexOf(value);
#else
        return s.IndexOf(value, comparisonType);
#endif
    }

#if !NET6_0_OR_GREATER
    public static async Task<T> WaitAsync<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<T>();
        using (cancellationToken.Register(static s => ((TaskCompletionSource<T>)s!).TrySetCanceled(), tcs))
        {
            return await (await Task.WhenAny(task, tcs.Task).ConfigureAwait(false)).ConfigureAwait(false);
        }
    }
#endif

    public static CancellationTokenRegistration RegisterWithCancellationTokenCallback(CancellationToken cancellationToken, Action<object?, CancellationToken> callback, object? state)
    {
        // Register overload that provides the CT to the callback required .NET 6 or greater.
        // Fallback to creating a closure in older platforms.
#if NET6_0_OR_GREATER
        return cancellationToken.Register(callback, state);
#else
        return cancellationToken.Register((state) => callback(state, cancellationToken), state);
#endif
    }

    public static Task<T> AwaitWithYieldAsync<T>(Task<T> callTask)
    {
        // A completed task doesn't need to yield because code after it isn't run in a continuation.
        if (callTask.IsCompleted)
        {
            return callTask;
        }

        return AwaitWithYieldAsyncCore(callTask);

        static async Task<T> AwaitWithYieldAsyncCore(Task<T> callTask)
        {
#if NET8_0_OR_GREATER
            return await callTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
#else
            var status = await callTask.ConfigureAwait(false);
            await Task.Yield();
            return status;
#endif
        }
    }
}
