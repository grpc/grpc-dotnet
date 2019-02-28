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
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Internal
{
    /// <summary>
    /// Uses reflection to get endpoint metadata and create delegates used to invoke service methods on .NET types.
    /// </summary>
    internal class ReflectionMethodModelFactory<TService> : IGrpcMethodModelFactory<TService>
    {
        public GrpcEndpointModel<ClientStreamingServerMethod<TService, TRequest, TResponse>> CreateClientStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateModelCore<ClientStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        public GrpcEndpointModel<DuplexStreamingServerMethod<TService, TRequest, TResponse>> CreateDuplexStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateModelCore<DuplexStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        public GrpcEndpointModel<ServerStreamingServerMethod<TService, TRequest, TResponse>> CreateServerStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateModelCore<ServerStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        public GrpcEndpointModel<UnaryServerMethod<TService, TRequest, TResponse>> CreateUnaryModel<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateModelCore<UnaryServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        private GrpcEndpointModel<TDelegate> CreateModelCore<TDelegate>(string methodName) where TDelegate : Delegate
        {
            var handlerMethod = typeof(TService).GetMethod(methodName);
            if (handlerMethod == null)
            {
                throw new InvalidOperationException($"Could not find '{methodName}' on {typeof(TService)}.");
            }

            var invoker = (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), handlerMethod);

            var metadata = new List<object>();
            // Add type metadata first so it has a lower priority
            metadata.AddRange(typeof(TService).GetCustomAttributes(inherit: true));
            // Add method metadata last so it has a higher priority
            metadata.AddRange(handlerMethod.GetCustomAttributes(inherit: true));

            return new GrpcEndpointModel<TDelegate>(invoker, metadata);
        }
    }
}
