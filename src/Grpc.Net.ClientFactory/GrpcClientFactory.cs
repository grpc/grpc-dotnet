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

using Grpc.Core;

namespace Grpc.Net.ClientFactory
{
    /// <summary>
    /// A factory abstraction for a component that can create gRPC client instances with custom
    /// configuration for a given logical name.
    /// </summary>
    public abstract class GrpcClientFactory
    {
        /// <summary>
        /// Create a gRPC client instance for the specified <typeparamref name="TClient"/> and configuration name.
        /// </summary>
        /// <typeparam name="TClient">The gRPC client type.</typeparam>
        /// <param name="name">The configuration name.</param>
        /// <returns>A gRPC client instance.</returns>
        public abstract TClient CreateClient<TClient>(string name) where TClient : ClientBase;
    }
}
