﻿#region Copyright notice and license

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

namespace Grpc.AspNetCore.Server.Model
{
    /// <summary>
    /// Defines a contract for specifying methods for <typeparamref name="TService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On application initialization, gRPC invokes all registered instances of <see cref="IServiceMethodProvider{TService}"/> to 
    /// perform method discovery. 
    /// <see cref="IServiceMethodProvider{TService}"/> instances are invoked in the order they are registered.
    /// </para>
    /// </remarks>
    public interface IServiceMethodProvider<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)]
#endif
        TService> where TService : class
    {
        /// <summary>
        /// Called to execute the provider.
        /// </summary>
        /// <param name="context">The <see cref="ServiceMethodProviderContext{TService}"/>.</param>
        void OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context);
    }
}
