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
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Grpc.AspNetCore.Server.Internal
{
    /// <summary>
    /// Service binder that is passed to ServiceImpl.BindService(ServiceBinderBase, ServiceImpl).
    /// This will execute the call handler factory and create call handlers.
    /// </summary>
    internal class GrpcServiceBinder<TService> : ServiceBinderBase where TService : class
    {
        private readonly IEndpointRouteBuilder _builder;
        private readonly ServerCallHandlerFactory<TService> _serverCallHandlerFactory;
        private readonly IGrpcMethodInvokerFactory<TService> _serviceInvokerFactory;

        internal IList<IEndpointConventionBuilder> EndpointConventionBuilders { get; } = new List<IEndpointConventionBuilder>();

        internal GrpcServiceBinder(IEndpointRouteBuilder builder, ServerCallHandlerFactory<TService> serverCallHandlerFactory, IGrpcMethodInvokerFactory<TService> serviceInvoker)
        {
            _builder = builder;
            _serverCallHandlerFactory = serverCallHandlerFactory;
            _serviceInvokerFactory = serviceInvoker;
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse> handler)
        {
            var callHandler = _serverCallHandlerFactory.CreateClientStreaming(method, _serviceInvokerFactory.CreateClientStreamingInvoker(method));
            EndpointConventionBuilders.Add(_builder.MapPost(method.FullName, callHandler.HandleCallAsync));
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TRequest, TResponse> handler)
        {
            var callHandler = _serverCallHandlerFactory.CreateDuplexStreaming(method, _serviceInvokerFactory.CreateDuplexStreamingInvoker(method));
            EndpointConventionBuilders.Add(_builder.MapPost(method.FullName, callHandler.HandleCallAsync));
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse> handler)
        {
            var callHandler = _serverCallHandlerFactory.CreateServerStreaming(method, _serviceInvokerFactory.CreateServerStreamingInvoker(method));
            EndpointConventionBuilders.Add(_builder.MapPost(method.FullName, callHandler.HandleCallAsync));
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse> handler)
        {
            var callHandler = _serverCallHandlerFactory.CreateUnary(method, _serviceInvokerFactory.CreateUnaryInvoker(method));
            EndpointConventionBuilders.Add(_builder.MapPost(method.FullName, callHandler.HandleCallAsync));
        }
    }
}
