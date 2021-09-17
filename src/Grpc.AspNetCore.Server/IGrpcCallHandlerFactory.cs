using System;
using System.Collections.Generic;
using System.Text;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server
{
    /// <summary>
    /// Extension point allowing libraries to access the gRPC request handlers without reflection
    /// </summary>
    /// <typeparam name="TService">The service type</typeparam>
    public interface IGrpcCallHandlerFactory<TService>
        where TService : class
    {
        /// <summary>
        /// Indicates whether unknown services are ignored
        /// </summary>
        bool IgnoreUnknownServices { get; }

        /// <summary>
        /// Indicates whether unknown methods are ignored
        /// </summary>
        bool IgnoreUnknownMethods { get; }

        /// <summary>
        /// Creates a request delegate for a unary method
        /// </summary>
        /// <typeparam name="TRequest">Request message type for this method.</typeparam>
        /// <typeparam name="TResponse">Response message type for this method.</typeparam>
        /// <param name="method">The method description.</param>
        /// <param name="invoker">The method invoker that is executed when the method is called.</param>
        /// <returns>An ASP.NET Core Request handler</returns>
        RequestDelegate CreateUnary<TRequest, TResponse>(
            Method<TRequest, TResponse> method, UnaryServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class;

        /// <summary>
        /// Creates a request delegate for a server streaming method
        /// </summary>
        /// <typeparam name="TRequest">Request message type for this method.</typeparam>
        /// <typeparam name="TResponse">Response message type for this method.</typeparam>
        /// <param name="method">The method description.</param>
        /// <param name="invoker">The method invoker that is executed when the method is called.</param>
        /// <returns>An ASP.NET Core Request handler</returns>
        RequestDelegate CreateServerStreaming<TRequest, TResponse>(
            Method<TRequest, TResponse> method, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class;

        /// <summary>
        /// Creates a request delegate for a client streaming method
        /// </summary>
        /// <typeparam name="TRequest">Request message type for this method.</typeparam>
        /// <typeparam name="TResponse">Response message type for this method.</typeparam>
        /// <param name="method">The method description.</param>
        /// <param name="invoker">The method invoker that is executed when the method is called.</param>
        /// <returns>An ASP.NET Core Request handler</returns>
        RequestDelegate CreateClientStreaming<TRequest, TResponse>(
            Method<TRequest, TResponse> method, ClientStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class;

        /// <summary>
        /// Creates a request delegate for a duplex streaming method
        /// </summary>
        /// <typeparam name="TRequest">Request message type for this method.</typeparam>
        /// <typeparam name="TResponse">Response message type for this method.</typeparam>
        /// <param name="method">The method description.</param>
        /// <param name="invoker">The method invoker that is executed when the method is called.</param>
        /// <returns>An ASP.NET Core Request handler</returns>
        RequestDelegate CreateDuplexStreaming<TRequest, TResponse>(
            Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class;

        /// <summary>
        /// Creates a request handler for an unimplemented method
        /// </summary>
        /// <returns>An ASP.NET Core Request handler</returns>
        RequestDelegate CreateUnimplementedMethod();

        /// <summary>
        /// Creates a request handler for an unimplemented service
        /// </summary>
        /// <returns>An ASP.NET Core Request handler</returns>
        RequestDelegate CreateUnimplementedService();
    }
}
