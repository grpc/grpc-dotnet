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
    /// An interface for creating models that describe and invoke service methods on .NET types.
    /// </summary>
    /// <typeparam name="TService">The service type.</typeparam>
    internal interface IGrpcMethodModelFactory<TService>
    {
        GrpcEndpointModel<UnaryServerMethod<TService, TRequest, TResponse>> CreateUnaryModel<TRequest, TResponse>(Method<TRequest, TResponse> method);
        GrpcEndpointModel<ClientStreamingServerMethod<TService, TRequest, TResponse>> CreateClientStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method);
        GrpcEndpointModel<ServerStreamingServerMethod<TService, TRequest, TResponse>> CreateServerStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method);
        GrpcEndpointModel<DuplexStreamingServerMethod<TService, TRequest, TResponse>> CreateDuplexStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method);
    }

    internal class GrpcEndpointModel<TInvoker> where TInvoker : Delegate
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
