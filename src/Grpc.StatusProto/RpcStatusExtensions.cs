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
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
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
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <example>
    /// For example, to retrieve any <see cref="Google.Rpc.ErrorInfo"/> that might be in the status details:
    /// <code>
    ///   var errorInfo = status.GetStatusDetail&lt;ErrorInfo&gt;();
    ///   if (errorInfo is not null) {
    ///      // ...
    ///   }
    /// </code>
    /// </example>
    /// </remarks>
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
    /// Create a <see cref="RpcException"/> from the <see cref="Google.Rpc.Status"/>
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
    /// throw new Google.Rpc.Status {
    ///   Code = (int) StatusCode.NotFound,
    ///   Message = "Simple error message",
    ///   Details = {
    ///     Any.Pack(new ErrorInfo { Domain = "example", Reason = "some reason" })
    ///   }
    /// }.ToRpcException();
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    /// <param name="status">The RPC status. Must not be null</param>
    /// <returns>A <see cref="RpcException"/> populated with the details from the status.</returns>
    public static RpcException ToRpcException(this Google.Rpc.Status status)
    {
        return status.ToRpcException((StatusCode)status.Code, status.Message);
    }

    /// <summary>
    /// Create a <see cref="RpcException"/> from the <see cref="Google.Rpc.Status"/>
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
    /// throw new Google.Rpc.Status {
    ///   Code = (int) StatusCode.NotFound,
    ///   Message = "Simple error message",
    ///   Details = {
    ///     Any.Pack(new ErrorInfo { Domain = "example", Reason = "some reason" })
    ///   }
    /// }.ToRpcException(StatusCode.NotFound, "status message");
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    /// <param name="status"></param>
    /// <param name="statusCode">The status to set in the contained <see cref="Grpc.Core.Status"/></param>
    /// <param name="message">The details to set in the contained <see cref="Grpc.Core.Status"/></param>
    /// <returns></returns>
    public static RpcException ToRpcException(this Google.Rpc.Status status, StatusCode statusCode, string message)
    {
        var metadata = new Metadata();
        metadata.SetRpcStatus(status);
        return new RpcException(
             new Grpc.Core.Status(statusCode, message),
             metadata);
    }

    /// <summary>
    /// Iterate over all the messages in the <see cref="Google.Rpc.Status.Details"/>
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Iterate over the messages in the <see cref="Google.Rpc.Status.Details"/> that are messages
    /// in the <see href="https://github.com/googleapis/googleapis/blob/master/google/rpc/error_details.proto">
    /// standard set of error types</see> defined in the richer error model. Any other messages found in
    /// the Details are ignored and not returned.
    /// </para>
    /// <para>
    /// <example>
    /// Example:
    /// <code>
    /// foreach (var msg in status.UnpackDetailMessage()) {
    ///   switch (msg) {
    ///     case ErrorInfo errorInfo:
    ///          // Handle errorInfo ...
    ///          break;
    ///     // Other cases ...
    ///   }
    /// }
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    /// <param name="status"></param>
    /// <returns></returns>
    public static IEnumerable<IMessage> UnpackDetailMessage(this Google.Rpc.Status status)
    {
        return status.UnpackDetailMessage(StandardErrorTypeRegistry.Registry);
    }

    /// <summary>
    /// Iterate over all the messages in the <see cref="Google.Rpc.Status.Details"/> that match types
    /// in the given <see cref="TypeRegistry"/>
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Iterate over the messages in the <see cref="Google.Rpc.Status.Details"/> that are messages
    /// in the given <see cref="TypeRegistry"/>. Any other messages found in the Details are ignored
    /// and not returned.  This allows iterating over custom messages if you are not using the
    /// standard set of error types defined in the rich error model.
    /// </para>
    /// <para>
    /// <example>
    /// Example:
    /// <code>
    /// TypeRegistry myTypes = TypeRegistry.FromMessages(
    ///   new MessageDescriptor[] {
    ///     FooMessage.Descriptor, BarMessage.Descriptor
    ///   });
    ///   
    /// foreach (var msg in status.UnpackDetailMessage(myTypes)) {
    ///   switch (msg) {
    ///     case FooMessage foo:
    ///          // Handle foo ...
    ///          break;
    ///     // Other cases ...
    ///   }
    /// }
    /// </code>
    /// </example>
    /// </para>
    /// </remarks>
    /// <param name="status"></param>
    /// <param name="registry"></param>
    /// <returns></returns>
    public static IEnumerable<IMessage> UnpackDetailMessage(this Google.Rpc.Status status, TypeRegistry registry)
    {
        foreach (var any in status.Details)
        {
            var msg = any.Unpack(registry);
            if (msg is not null)
            {
                yield return msg;
            }
        }
    }
}
