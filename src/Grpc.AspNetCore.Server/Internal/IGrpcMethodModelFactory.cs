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
    public interface IGrpcMethodModelFactory<TService>
    {
        /// <summary>
        /// Creates a model that describe and invoke a unary operation.
        /// </summary>
        /// <typeparam name="TRequest">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="method">Unary remote method description.</param>
        /// <returns>A model that describe and invoke a unary operation.</returns>
        GrpcEndpointModel<UnaryServerMethod<TService, TRequest, TResponse>> CreateUnaryModel<TRequest, TResponse>(Method<TRequest, TResponse> method);


        /// <summary>
        /// Creates a model that describe and invoke a client streaming operation.
        /// </summary>
        /// <typeparam name="TRequest">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="method">Client streaming remote method description.</param>
        /// <returns>A model that describe and invoke a client streaming operation.</returns>
        GrpcEndpointModel<ClientStreamingServerMethod<TService, TRequest, TResponse>> CreateClientStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method);

        /// <summary>
        /// Creates a model that describe and invoke a server streaming operation.
        /// </summary>
        /// <typeparam name="TRequest">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="method">Server streaming remote method description.</param>
        /// <returns>A model that describe and invoke a server streaming operation.</returns>
        GrpcEndpointModel<ServerStreamingServerMethod<TService, TRequest, TResponse>> CreateServerStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method);

        /// <summary>
        /// Creates a model that describe and invoke a duplex streaming operation.
        /// </summary>
        /// <typeparam name="TRequest">The request type.</typeparam>
        /// <typeparam name="TResponse">The response type.</typeparam>
        /// <param name="method">Duplex streaming remote method description.</param>
        /// <returns>A model that describe and invoke a duplex streaming operation.</returns>
        GrpcEndpointModel<DuplexStreamingServerMethod<TService, TRequest, TResponse>> CreateDuplexStreamingModel<TRequest, TResponse>(Method<TRequest, TResponse> method);
    }

    /// <summary>
    /// Describes a service method on a .NET type and provides a method invoker.
    /// </summary>
    /// <typeparam name="TInvoker">The method invoker delegate.</typeparam>
    public class GrpcEndpointModel<TInvoker> where TInvoker : Delegate
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="invoker"></param>
        /// <param name="metadata"></param>
        public GrpcEndpointModel(TInvoker invoker, List<object> metadata)
        {
            Invoker = invoker;
            Metadata = metadata;
        }

        /// <summary>
        /// Gets a delegate that invokes the .NET method. 
        /// </summary>
        public TInvoker Invoker { get; }
        
        /// <summary>
        /// Gets metadata related to the service method.
        /// </summary>
        public List<object> Metadata { get; }
    }
}
