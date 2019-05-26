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

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal class ProviderServiceBinder<TService> : ServiceBinderBase where TService : class
    {
        private readonly ServiceMethodProviderContext<TService> _context;

        internal ProviderServiceBinder(ServiceMethodProviderContext<TService> context)
        {
            _context = context;
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse> handler)
        {
            var model = CreateModelCore<ClientStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
            _context.AddClientStreamingMethod<TRequest, TResponse>(method, model.Metadata, model.Invoker);
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TRequest, TResponse> handler)
        {
            var model = CreateModelCore<DuplexStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
            _context.AddDuplexStreamingMethod<TRequest, TResponse>(method, model.Metadata, model.Invoker);
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse> handler)
        {
            var model = CreateModelCore<ServerStreamingServerMethod<TService, TRequest, TResponse>>(method.Name);
            _context.AddServerStreamingMethod<TRequest, TResponse>(method, model.Metadata, model.Invoker);
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse> handler)
        {
            var model = CreateModelCore<UnaryServerMethod<TService, TRequest, TResponse>>(method.Name);
            _context.AddUnaryMethod<TRequest, TResponse>(method, model.Metadata, model.Invoker);
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

        private class GrpcEndpointModel<TInvoker> where TInvoker : Delegate
        {
            public GrpcEndpointModel(TInvoker invoker, List<object> metadata)
            {
                Invoker = invoker;
                Metadata = metadata;
            }

            public TInvoker Invoker { get; }
            public List<object> Metadata { get; }
        }
    }
}
