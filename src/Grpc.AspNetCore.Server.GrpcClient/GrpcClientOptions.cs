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
using System.Security.Cryptography.X509Certificates;

namespace Grpc.AspNetCore.Server.GrpcClient
{
    /// <summary>
    /// Options used to configure a gRPC client.
    /// </summary>
    public class GrpcClientOptions
    {
        /// <summary>
        /// The base address to use when making gRPC calls.
        /// </summary>
        public Uri? BaseAddress { get; set; }

        /// <summary>
        /// The client certificate to use when making gRPC calls.
        /// </summary>
        public X509Certificate? Certificate { get; set; }

        /// <summary>
        /// A flag that indicates whether the call cancellation token should be propagated to client calls. Defaults to true.
        /// </summary>
        public bool PropagateCancellationToken { get; set; } = true;

        /// <summary>
        /// A flag that indicates whether the call deadline should be propagated to client calls. Defaults to true.
        /// </summary>
        public bool PropagateDeadline { get; set; } = true;

        // This property is set internally. It is used to check whether named configuration was explicitly set by the user
        internal bool ExplicitlySet { get; set; }
    }
}
