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

namespace Grpc.AspNetCore.ClientFactory
{
    /// <summary>
    /// Options used to configure gRPC call context propagation.
    /// </summary>
    public class GrpcContextPropagationOptions
    {
        /// <summary>
        /// Gets or sets a value that determines if context not found errors are suppressed.
        /// <para>
        /// When <see langword="false"/>, the client will thrown an error if it is unable to
        /// find a call context when propagating values to a gRPC call.
        /// Otherwise, the error is suppressed and the gRPC call will be made without context
        /// propagation.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Call context propagation will error by default if propagation can't happen because
        /// the call context wasn't found. This typically happens when a client is used
        /// outside the context of an executing gRPC service.
        /// </para>
        /// <para>
        /// Suppressing context not found errors allows a client with propagation enabled to be
        /// used outside the context of an executing gRPC service.
        /// </para>
        /// </remarks>
        /// <value>The default value is <see langword="false"/>.</value>
        public bool SuppressContextNotFoundErrors { get; set; }
    }
}
