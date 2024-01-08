// Copyright 2023 gRPC authors.
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

using Google.Rpc;
using Grpc.Shared;

namespace Grpc.Core;

/// <summary>
/// Extensions methods for converting <see cref="Exception"/> to <see cref="DebugInfo"/>.
/// </summary>
public static class ExceptionExtensions
{
    /// <summary>
    /// Create a <see cref="DebugInfo"/> from an <see cref="Exception"/>,
    /// populating the Message and StackTrace from the exception.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <example>
    /// For example:
    /// <code>
    /// try
    /// {
    ///     /* ... */
    /// }
    /// catch (Exception e)
    /// {
    ///     Google.Rpc.Status status = new()
    ///     {
    ///         Code = (int)StatusCode.Internal,
    ///         Message = "Internal error",
    ///         Details =
    ///         {
    ///             // populate debugInfo from the exception
    ///             Any.Pack(e.ToRpcDebugInfo())
    ///         }
    ///     };
    ///     // ...
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="exception">The exception to create a <see cref="DebugInfo"/> from.</param>
    /// <param name="innerDepth">Maximum number of inner exceptions to include in the StackTrace. Defaults
    /// to not including any inner exceptions</param>
    /// <returns>
    /// A new <see cref="DebugInfo"/> populated from the exception.
    /// </returns>
    public static DebugInfo ToRpcDebugInfo(this Exception exception, int innerDepth = 0)
    {
        ArgumentNullThrowHelper.ThrowIfNull(exception);

        var debugInfo = new DebugInfo();

        var message = exception.Message;
        var name = exception.GetType().FullName;

        // Populate the Detail from the exception type and message
        debugInfo.Detail = message is null ? name : name + ": " + message;

        // Populate the StackEntries from the exception StackTrace
        if (exception.StackTrace is not null)
        {
            var sr = new StringReader(exception.StackTrace);
            var entry = sr.ReadLine();
            while (entry is not null)
            {
                debugInfo.StackEntries.Add(entry);
                entry = sr.ReadLine();
            }
        }

        // Add inner exceptions to the StackEntries
        var inner = exception.InnerException;
        while (innerDepth > 0 && inner is not null)
        {
            message = inner.Message;
            name = inner.GetType().FullName;
            debugInfo.StackEntries.Add("InnerException: " + (message is null ? name : name + ": " + message));

            if (inner.StackTrace is not null)
            {
                var sr = new StringReader(inner.StackTrace);
                var entry = sr.ReadLine();
                while (entry is not null)
                {
                    debugInfo.StackEntries.Add(entry);
                    entry = sr.ReadLine();
                }
            }

            inner = inner.InnerException;
            --innerDepth;
        }

        return debugInfo;
    }
}
