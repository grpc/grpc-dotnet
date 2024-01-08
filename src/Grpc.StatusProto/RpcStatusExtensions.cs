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
/// Extensions methods for converting <see cref="Google.Rpc.Status"/> to <see cref="RpcException"/>.
/// </summary>
public static class RpcStatusExtensions
{
    /// <summary>
    /// Create a <see cref="RpcException"/> from the <see cref="Google.Rpc.Status"/>.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Grpc.Core.Status.StatusCode"/> and <see cref="Grpc.Core.Status.Detail"/> on
    /// <see cref="Grpc.Core.Status"/> within the exception are populated from the details from
    /// <see cref="Google.Rpc.Status"/>.
    /// </para>
    /// <para>
    /// <example>
    /// Example:
    /// <code>
    /// var status = new Google.Rpc.Status
    /// {
    ///     Code = (int) StatusCode.NotFound,
    ///     Message = "Simple error message",
    ///     Details =
    ///     {
    ///         Any.Pack(new ErrorInfo { Domain = "example", Reason = "some reason" })
    ///     }
    /// };
    ///
    /// throw status.ToRpcException();
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    /// <param name="status">The <see cref="Google.Rpc.Status"/>. Must not be null</param>
    /// <returns>A <see cref="RpcException"/> populated with the details from the status.</returns>
    public static RpcException ToRpcException(this Google.Rpc.Status status)
    {
        ArgumentNullThrowHelper.ThrowIfNull(status);

        // Both Grpc.Core.StatusCode and Google.Rpc.Code define enums for a common
        // set of status codes such as "NotFound", "PermissionDenied", etc. They have the same
        // values and are based on the codes defined "grpc/status.h"
        //
        // However applications can use a different domain of values if they want and and as
        // long as their services are mutually compatible, things will work fine.
        //
        // If an application wants to explicitly set different status codes in Grpc.Core.Status
        // and Google.Rpc.Status then use the ToRpcException below that takes additional parameters.
        //
        // Check here that we can convert Google.Rpc.Status.Code to Grpc.Core.StatusCode,
        // and if not use StatusCode.Unknown.
        var statusCode = Enum.IsDefined(typeof(StatusCode), status.Code) ? (StatusCode)status.Code : StatusCode.Unknown;
        return status.ToRpcException(statusCode, status.Message);
    }

    /// <summary>
    /// Create a <see cref="RpcException"/> from the <see cref="Google.Rpc.Status"/>.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="Grpc.Core.Status.StatusCode"/> and <see cref="Grpc.Core.Status.Detail"/> in the
    /// <see cref="Grpc.Core.Status"/> within the exception are populated from the details in the
    /// <see cref="Google.Rpc.Status"/>
    /// </para>
    /// <para>
    /// <example>
    /// Example:
    /// <code>
    /// var status = new Google.Rpc.Status
    /// {
    ///     Code = (int) StatusCode.NotFound,
    ///     Message = "Simple error message",
    ///     Details =
    ///     {
    ///         Any.Pack(new ErrorInfo { Domain = "example", Reason = "some reason" })
    ///     }
    /// };
    ///
    /// throw status.ToRpcException(StatusCode.NotFound, "status message");
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    /// <param name="status">The <see cref="Google.Rpc.Status"/>. Must not be null</param>
    /// <param name="statusCode">The status to set in the exception's <see cref="Grpc.Core.Status"/></param>
    /// <param name="message">The details to set in the exception's <see cref="Grpc.Core.Status"/></param>
    /// <returns>A <see cref="RpcException"/> populated with the details from the status.</returns>
    public static RpcException ToRpcException(this Google.Rpc.Status status, StatusCode statusCode, string message)
    {
        ArgumentNullThrowHelper.ThrowIfNull(status);

        var metadata = new Metadata();
        metadata.SetRpcStatus(status);
        return new RpcException(new Status(statusCode, message), metadata);
    }
}
