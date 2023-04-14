#region Copyright notice and license

// Copyright 2019 The gRPC Authors
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

using Grpc.AspNetCore.Server.Internal;
using Grpc.Shared;
using Microsoft.AspNetCore.Http;

namespace Grpc.Core;

/// <summary>
/// Extension methods for ServerCallContext.
/// </summary>
public static class ServerCallContextExtensions
{
    internal const string HttpContextKey = "__HttpContext";

    /// <summary>
    /// Retrieve the <see cref="HttpContext"/> from the a call's <see cref="ServerCallContext"/>.
    /// The HttpContext is only available when gRPC services are hosted by ASP.NET Core. An error will be
    /// thrown if this method is used outside of ASP.NET Core.
    /// Note that read-only usage of HttpContext is recommended as changes to the HttpContext are not synchronized
    /// with the ServerCallContext.
    /// </summary>
    /// <param name="serverCallContext">The <see cref="ServerCallContext"/>.</param>
    /// <returns>The call's <see cref="HttpContext"/>.</returns>
    public static HttpContext GetHttpContext(this ServerCallContext serverCallContext)
    {
        ArgumentNullThrowHelper.ThrowIfNull(serverCallContext);

        // Attempt to quickly get HttpContext from known call context type.
        if (serverCallContext is HttpContextServerCallContext httpContextServerCallContext)
        {
            return httpContextServerCallContext.HttpContext;
        }

        // Fallback to getting HttpContext from user state.
        // This is to support custom gRPC invokers that replace the default server call context.
        // They must place the HttpContext in UserState with the `__HttpContext` key.
        if (serverCallContext.UserState != null &&
            serverCallContext.UserState.TryGetValue(HttpContextKey, out var c) &&
            c is HttpContext httpContext)
        {
            return httpContext;
        }

        throw new InvalidOperationException("Could not get HttpContext from ServerCallContext. HttpContext can only be accessed when gRPC services are hosted by ASP.NET Core.");
    }
}
