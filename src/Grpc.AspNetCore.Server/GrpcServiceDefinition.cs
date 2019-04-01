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
using System.Collections.Generic;
using System.Linq;
using Grpc.Core;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Represents a gRPC service registered with the application.
    /// </summary>
    public class GrpcServiceDefinition
    {
        internal GrpcServiceDefinition(Type serviceType, IEnumerable<IMethod> methods)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (methods == null)
            {
                throw new ArgumentNullException(nameof(methods));
            }

            ServiceType = serviceType;
            Methods = methods.ToList();
        }

        /// <summary>
        /// Gets the gRPC service type.
        /// </summary>
        public Type ServiceType { get; }

        /// <summary>
        /// Gets the service's methods.
        /// </summary>
        public IReadOnlyList<IMethod> Methods { get; }
    }
}
