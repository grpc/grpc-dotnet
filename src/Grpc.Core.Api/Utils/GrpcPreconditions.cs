#region Copyright notice and license

// Copyright 2015 gRPC authors.
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

#endregion

using System;

namespace Grpc.Core.Utils;

/// <summary>
/// Utility methods to simplify checking preconditions in the code.
/// </summary>
public static class GrpcPreconditions
{
    /// <summary>
    /// Throws <see cref="ArgumentException"/> if condition is false.
    /// </summary>
    /// <param name="condition">The condition.</param>
    public static void CheckArgument(bool condition)
    {
        if (!condition)
        {
            Throw();
        }

        static void Throw()
            => throw new ArgumentException();
    }

    /// <summary>
    /// Throws <see cref="ArgumentException"/> with given message if condition is false.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="errorMessage">The error message.</param>
    public static void CheckArgument(bool condition, string errorMessage)
    {
        if (!condition)
        {
            Throw(errorMessage);
        }

        static void Throw(string errorMessage)
            => throw new ArgumentException(errorMessage);
    }

    /// <summary>
    /// Throws <see cref="ArgumentNullException"/> if reference is null.
    /// </summary>
    /// <param name="reference">The reference.</param>
#if DEBUG
    // this method is on the public API; don't want to break anything
    // external, but: prevent additional usage locally (i.e. in DEBUG mode)
    [Obsolete("Specify parameter name when possible", error: true)]
#endif
    public static T CheckNotNull<T>(T reference)
    {
        if (reference is null)
        {
            Throw();
        }
        return reference;

        static void Throw()
            => throw new ArgumentNullException();
    }

    /// <summary>
    /// Throws <see cref="ArgumentNullException"/> if reference is null.
    /// </summary>
    /// <param name="reference">The reference.</param>
    /// <param name="paramName">The parameter name.</param>
    public static T CheckNotNull<T>(T reference, string paramName)
    {
        if (reference is null)
        {
            Throw(paramName);
        }
        return reference;

        static void Throw(string paramName)
            => throw new ArgumentNullException(paramName);
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if condition is false.
    /// </summary>
    /// <param name="condition">The condition.</param>
    public static void CheckState(bool condition)
    {
        if (!condition)
        {
            Throw();
        }
        static void Throw()
            => throw new InvalidOperationException();
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> with given message if condition is false.
    /// </summary>
    /// <param name="condition">The condition.</param>
    /// <param name="errorMessage">The error message.</param>
    public static void CheckState(bool condition, string errorMessage)
    {
        if (!condition)
        {
            Throw(errorMessage);
        }
        static void Throw(string errorMessage)
            => throw new InvalidOperationException(errorMessage);
    }
}
