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

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

namespace Grpc.StatusProto;

/// <summary>
/// Extensions for <see cref="Google.Rpc.Status"/> to retrieve detailed error information.
/// Based on ideas from:
/// https://github.com/googleapis/gax-dotnet/blob/main/Google.Api.Gax.Grpc/RpcExceptionExtensions.cs
/// </summary>
public static class RpcStatusExtensions
{
    /// <summary>
    /// Retrieves the error details of type <typeparamref name="T"/> from the <see cref="Google.Rpc.Status"/>
    /// message.
    /// </summary>
    /// <typeparam name="T">The message type to decode from within the error details.</typeparam>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The first error details of type <typeparamref name="T"/> found, or null if not present</returns>
    public static T? GetStatusDetail<T>(this Google.Rpc.Status status) where T : class, IMessage<T>, new()
    {
        var expectedName = new T().Descriptor.FullName;
        var any = status.Details.FirstOrDefault(a => Any.GetTypeName(a.TypeUrl) == expectedName);
        if (any is null)
        {
            return null;
        }
        return any.Unpack<T>();
    }

    /// <summary>
    /// Retrieves the <see cref="BadRequest"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC Status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="BadRequest"/> message specified in the exception, or null if not found.</returns>
    public static BadRequest? GetBadRequest(this Google.Rpc.Status status) => GetStatusDetail<BadRequest>(status);

    /// <summary>
    /// Retrieves the <see cref="ErrorInfo"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="ErrorInfo"/> message specified in the exception, or null if not found.</returns>
    public static ErrorInfo? GetErrorInfo(this Google.Rpc.Status status) => GetStatusDetail<ErrorInfo>(status);

    /// <summary>
    /// Retrieves the <see cref="RetryInfo"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="RetryInfo"/> message specified in the exception, or null if not found.</returns>
    public static RetryInfo? GetRetryInfo(this Google.Rpc.Status status) => GetStatusDetail<RetryInfo>(status);

    /// <summary>
    /// Retrieves the <see cref="DebugInfo"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="DebugInfo"/> message specified in the exception, or null if not found.</returns>
    public static DebugInfo? GetDebugInfo(this Google.Rpc.Status status) => GetStatusDetail<DebugInfo>(status);

    /// <summary>
    /// Retrieves the <see cref="QuotaFailure"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="QuotaFailure"/> message specified in the exception, or null if not found.</returns>
    public static QuotaFailure? GetQuotaFailure(this Google.Rpc.Status status) => GetStatusDetail<QuotaFailure>(status);

    /// <summary>
    /// Retrieves the <see cref="PreconditionFailure"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="PreconditionFailure"/> message specified in the exception, or null if not found.</returns>
    public static PreconditionFailure? GetPreconditionFailure(this Google.Rpc.Status status) => GetStatusDetail<PreconditionFailure>(status);

    /// <summary>
    /// Retrieves the <see cref="RequestInfo"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="RequestInfo"/> message specified in the exception, or null if not found.</returns>
    public static RequestInfo? GetRequestInfo(this Google.Rpc.Status status) => GetStatusDetail<RequestInfo>(status);

    /// <summary>
    /// Retrieves the <see cref="ResourceInfo"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="ResourceInfo"/> message specified in the exception, or null if not found.</returns>
    public static ResourceInfo? GetResourceInfo(this Google.Rpc.Status status) => GetStatusDetail<ResourceInfo>(status);

    /// <summary>
    /// Retrieves the <see cref="Help"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="Help"/> message specified in the exception, or null if not found.</returns>
    public static Help? GetHelp(this Google.Rpc.Status status) => GetStatusDetail<Help>(status);

    /// <summary>
    /// Retrieves the <see cref="LocalizedMessage"/> message containing extended error information
    /// from the <see cref="Google.Rpc.Status"/>, if present.
    /// </summary>
    /// <param name="status">The RPC status to retrieve details from. Must not be null.</param>
    /// <returns>The <see cref="LocalizedMessage"/> message specified in the exception, or null if not found.</returns>
    public static LocalizedMessage? GetLocalizedMessage(this Google.Rpc.Status status) => GetStatusDetail<LocalizedMessage>(status);

    /// <summary>
    /// Create a <see cref="Grpc.Core.RpcException"/> from the <see cref="Google.Rpc.Status"/>
    /// </summary>
    /// <param name="status">The RPC status. Must not be null</param>
    /// <returns>A <see cref="Grpc.Core.RpcException"/> populated with the details from the status.</returns>
    public static RpcException ToRpcException(this Google.Rpc.Status status)
    {
        var metadata = new Metadata();
        metadata.SetRpcStatus(status);
        return new RpcException(
             new Grpc.Core.Status((StatusCode)status.Code, status.Message),
             metadata);
    }
}
