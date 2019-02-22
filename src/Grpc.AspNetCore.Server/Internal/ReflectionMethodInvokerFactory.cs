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
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Internal
{
    /// <summary>
    /// Uses reflection to create delegates used to invoke service methods on .NET types.
    /// </summary>
    internal class ReflectionMethodInvokerFactory<TService> : IGrpcMethodInvokerFactory<TService>
    {
        public ClientStreamingServerMethod<TService, TRequest, TResponse> CreateClientStreamingInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateInvokerCore<ClientStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        public DuplexStreamingServerMethod<TService, TRequest, TResponse> CreateDuplexStreamingInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateInvokerCore<DuplexStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        public ServerStreamingServerMethod<TService, TRequest, TResponse> CreateServerStreamingInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateInvokerCore<ServerStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        public UnaryServerMethod<TService, TRequest, TResponse> CreateUnaryInvoker<TRequest, TResponse>(Method<TRequest, TResponse> method)
        {
            return CreateInvokerCore<UnaryServerMethod<TService, TRequest, TResponse>>(method.Name);
        }

        private TDelegate CreateInvokerCore<TDelegate>(string methodName) where TDelegate : Delegate
        {
            var handlerMethod = typeof(TService).GetMethod(methodName);
            if (handlerMethod == null)
            {
                throw new InvalidOperationException($"Could not find '{methodName}' on {typeof(TService)}.");
            }

            return (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), handlerMethod);
        }
    }
}
