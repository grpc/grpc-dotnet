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

using System;
using Grpc.AspNetCore.Server.Internal;
using Microsoft.AspNetCore.Http;

namespace Grpc.Core
{
    /// <summary>
    /// Extension methods for ServerCallContext.
    /// </summary>
    public static class ServerCallContextExtensions
    {
        /// <summary>
        /// Retrieve the <see cref="HttpContext"/> from a <see cref="ServerCallContext"/> if possible. Note that read-only access is
        /// recommended as changes to the HttpContext is not synchronized with the ServerCallContext. The HttpContext is only available
        /// when using gRPC with ASP.NET Core.
        /// </summary>
        /// <param name="serverCallContext">The <see cref="ServerCallContext"/> to extract from.</param>
        /// <returns>The extracted <see cref="HttpContext"/>. If it cannot be extracted, an error will be thrown.</returns>
        public static HttpContext GetHttpContext(this ServerCallContext serverCallContext)
        {
            var httpContextServerCallContext = serverCallContext as HttpContextServerCallContext;
            if (httpContextServerCallContext == null)
            {
                throw new InvalidOperationException("Could not get HttpContext from ServerCallContext. HttpContext can only be accessed when gRPC services are hosted by Kestrel.");
            }

            return httpContextServerCallContext.HttpContext;
        }
    }
}
