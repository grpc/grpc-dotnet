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
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Options used to configure the binding of a gRPC service.
    /// </summary>
    public class GrpcBindingOptions<TService> where TService : class
    {
        /// <summary>
        /// The action invoked to get service metadata via <see cref="ServiceBinderBase"/>.
        /// </summary>
        public Action<ServiceBinderBase, TService?>? BindAction { get; set; }

        // Currently internal. It is set in tests via InternalVisibleTo. Can be made public if there is demand for it
        internal IGrpcMethodModelFactory<TService>? ModelFactory { get; set; }
    }
}
