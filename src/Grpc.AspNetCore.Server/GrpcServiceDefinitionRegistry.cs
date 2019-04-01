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

using System.Collections.Generic;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// gRPC services registered in the application.
    /// </summary>
    public class GrpcServiceDefinitionRegistry
    {
        private readonly List<GrpcServiceDefinition> _serviceDefinitions;

        internal GrpcServiceDefinitionRegistry()
        {
            _serviceDefinitions = new List<GrpcServiceDefinition>();
        }

        /// <summary>
        /// Gets a collection of <see cref="GrpcServiceDefinition"/> that represent gRPC services registered in the application.
        /// </summary>
        public IReadOnlyList<GrpcServiceDefinition> ServiceDefinitions => _serviceDefinitions;

        internal void AddServiceDefinition(GrpcServiceDefinition serviceDefinition)
        {
            _serviceDefinitions.Add(serviceDefinition);
        }
    }
}
