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
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Provides methods to create a gRPC service.
    /// </summary>
    public interface IGrpcServiceActivator
    {
        /// <summary>
        /// Creates a gRPC service.
        /// </summary>
        /// <param name="context">The <see cref="ServerCallContext"/> for the executing action</param>
        /// <param name="grpcServiceType">The gRPC service type</param>
        object Create(ServerCallContext context, Type grpcServiceType);

        /// <summary>
        /// Releases a gRPC service.
        /// </summary>
        /// <param name="grpcService">The gRPC service to release.</param>
        ValueTask ReleaseAsync(object grpcService);
    }
}