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

using System.Diagnostics.CodeAnalysis;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Grpc.AspNetCore.Server.Model;

/// <summary>
/// A context for <see cref="IServiceMethodProvider{TService}"/>.
/// </summary>
/// <typeparam name="TService">Service type for the context.</typeparam>
public class ServiceMethodProviderContext<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService> where TService : class
{
    private readonly ServerCallHandlerFactory<TService> _serverCallHandlerFactory;

    internal ServiceMethodProviderContext(ServerCallHandlerFactory<TService> serverCallHandlerFactory)
    {
        Methods = new List<MethodModel>();
        _serverCallHandlerFactory = serverCallHandlerFactory;
    }

    internal List<MethodModel> Methods { get; }

    /// <summary>
    /// Adds a unary method to a service.
    /// </summary>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="method">The method description.</param>
    /// <param name="metadata">The method metadata. This metadata can be used by routing and middleware when invoking a gRPC method.</param>
    /// <param name="invoker">The method invoker that is executed when the method is called.</param>
    public void AddUnaryMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, UnaryServerMethod<TService, TRequest, TResponse> invoker)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateUnary<TRequest, TResponse>(method, invoker);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), metadata, callHandler.HandleCallAsync);
    }

    /// <summary>
    /// Adds a server streaming method to a service.
    /// </summary>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="method">The method description.</param>
    /// <param name="metadata">The method metadata. This metadata can be used by routing and middleware when invoking a gRPC method.</param>
    /// <param name="invoker">The method invoker that is executed when the method is called.</param>
    public void AddServerStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateServerStreaming<TRequest, TResponse>(method, invoker);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), metadata, callHandler.HandleCallAsync);
    }

    /// <summary>
    /// Adds a client streaming method to a service.
    /// </summary>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="method">The method description.</param>
    /// <param name="metadata">The method metadata. This metadata can be used by routing and middleware when invoking a gRPC method.</param>
    /// <param name="invoker">The method invoker that is executed when the method is called.</param>
    public void AddClientStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, ClientStreamingServerMethod<TService, TRequest, TResponse> invoker)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateClientStreaming<TRequest, TResponse>(method, invoker);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), metadata, callHandler.HandleCallAsync);
    }

    /// <summary>
    /// Adds a duplex streaming method to a service.
    /// </summary>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="method">The method description.</param>
    /// <param name="metadata">The method metadata. This metadata can be used by routing and middleware when invoking a gRPC method.</param>
    /// <param name="invoker">The method invoker that is executed when the method is called.</param>
    public void AddDuplexStreamingMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, IList<object> metadata, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
        where TRequest : class
        where TResponse : class
    {
        var callHandler = _serverCallHandlerFactory.CreateDuplexStreaming<TRequest, TResponse>(method, invoker);
        AddMethod(method, RoutePatternFactory.Parse(method.FullName), metadata, callHandler.HandleCallAsync);
    }

    /// <summary>
    /// Adds a method to a service. This method is handled by the specified <see cref="RequestDelegate"/>.
    /// This overload of <c>AddMethod</c> is intended for advanced scenarios where raw processing of HTTP requests
    /// is desired.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <typeparam name="TRequest">Request message type for this method.</typeparam>
    /// <typeparam name="TResponse">Response message type for this method.</typeparam>
    /// <param name="method">The method description.</param>
    /// <param name="pattern">The method pattern. This pattern is used by routing to match the method to an HTTP request.</param>
    /// <param name="metadata">The method metadata. This metadata can be used by routing and middleware when invoking a gRPC method.</param>
    /// <param name="invoker">The <see cref="RequestDelegate"/> that is executed when the method is called.</param>
    public void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, RoutePattern pattern, IList<object> metadata, RequestDelegate invoker)
        where TRequest : class
        where TResponse : class
    {
        var methodModel = new MethodModel(method, pattern, metadata, invoker);
        Methods.Add(methodModel);
    }
}
