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

using Grpc.Shared;

namespace Grpc.Core;

/// <summary>
/// Extension methods for getting <see cref="Google.Rpc.Status"/> from <see cref="RpcException"/>.
/// </summary>
public static class RpcExceptionExtensions
{
    /// <summary>
    /// Retrieves the <see cref="Google.Rpc.Status"/> message containing extended error information
    /// from the trailers in an <see cref="RpcException"/>, if present.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <param name="ex">The <see cref="RpcException"/> to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="Google.Rpc.Status"/> message specified in the exception, or null
    /// if there is no such information.</returns>
    public static Google.Rpc.Status? GetRpcStatus(this RpcException ex)
    {
        ArgumentNullThrowHelper.ThrowIfNull(ex);
        return ex.Trailers.GetRpcStatus();
    }
}
