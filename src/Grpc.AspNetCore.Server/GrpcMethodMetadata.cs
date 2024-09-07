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

using System.Diagnostics.CodeAnalysis;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared;

namespace Grpc.AspNetCore.Server;

/// <summary>
/// Metadata for a gRPC method endpoint.
/// </summary>
public sealed class GrpcMethodMetadata
{
    /// <summary>
    /// Creates a new instance of <see cref="GrpcMethodMetadata"/> with the provided service type and method.
    /// </summary>
    /// <param name="serviceType">The implementing service type.</param>
    /// <param name="method">The method representation.</param>
    public GrpcMethodMetadata([DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] Type serviceType, IMethod method)
    {
        ArgumentNullThrowHelper.ThrowIfNull(serviceType);
        ArgumentNullThrowHelper.ThrowIfNull(method);

        ServiceType = serviceType;
        Method = method;
    }

    /// <summary>
    /// Gets the implementing service type.
    /// </summary>
    [DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)]
    public Type ServiceType { get; }

    /// <summary>
    /// Gets the method representation.
    /// </summary>
    public IMethod Method { get; }
}
