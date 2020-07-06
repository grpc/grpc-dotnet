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

using System;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Shared.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.Internal
{
    /// <summary>
    /// Creates server call handlers. Provides a place to get services that call handlers will use.
    /// </summary>
    internal partial class ServerCallHandlerFactory<TService> where TService : class
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IGrpcServiceActivator<TService> _serviceActivator;
        private readonly GrpcServiceOptions _globalOptions;
        private readonly GrpcServiceOptions<TService> _serviceOptions;

        public ServerCallHandlerFactory(
            ILoggerFactory loggerFactory,
            IOptions<GrpcServiceOptions> globalOptions,
            IOptions<GrpcServiceOptions<TService>> serviceOptions,
            IGrpcServiceActivator<TService> serviceActivator)
        {
            _loggerFactory = loggerFactory;
            _serviceActivator = serviceActivator;
            _serviceOptions = serviceOptions.Value;
            _globalOptions = globalOptions.Value;
        }

        // Internal for testing
        internal MethodOptions CreateMethodOptions()
        {
            return MethodOptions.Create(new[] { _globalOptions, _serviceOptions });
        }

        public UnaryServerCallHandler<TService, TRequest, TResponse> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var options = CreateMethodOptions();
            var methodInvoker = new UnaryServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

            return new UnaryServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
        }

        public ClientStreamingServerCallHandler<TService, TRequest, TResponse> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var options = CreateMethodOptions();
            var methodInvoker = new ClientStreamingServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

            return new ClientStreamingServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
        }

        public DuplexStreamingServerCallHandler<TService, TRequest, TResponse> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var options = CreateMethodOptions();
            var methodInvoker = new DuplexStreamingServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

            return new DuplexStreamingServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
        }

        public ServerStreamingServerCallHandler<TService, TRequest, TResponse> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            var options = CreateMethodOptions();
            var methodInvoker = new ServerStreamingServerMethodInvoker<TService, TRequest, TResponse>(invoker, method, options, _serviceActivator);

            return new ServerStreamingServerCallHandler<TService, TRequest, TResponse>(methodInvoker, _loggerFactory);
        }

        public RequestDelegate CreateUnimplementedMethod()
        {
            var logger = _loggerFactory.CreateLogger<ServerCallHandlerFactory<TService>>();

            return httpContext =>
            {
                // CORS preflight request should be handled by CORS middleware.
                // If it isn't then return 404 from endpoint request delegate.
                if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }

                GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

                var unimplementedMethod = httpContext.Request.RouteValues["unimplementedMethod"]?.ToString() ?? "<unknown>";
                Log.MethodUnimplemented(logger, unimplementedMethod);
                GrpcEventSource.Log.CallUnimplemented(httpContext.Request.Path.Value);

                GrpcProtocolHelpers.SetStatus(GrpcProtocolHelpers.GetTrailersDestination(httpContext.Response), new Status(StatusCode.Unimplemented, "Method is unimplemented."));
                return Task.CompletedTask;
            };
        }

        public bool IgnoreUnknownServices => _globalOptions.IgnoreUnknownServices ?? false;
        public bool IgnoreUnknownMethods => _serviceOptions.IgnoreUnknownServices ?? _globalOptions.IgnoreUnknownServices  ?? false;

        public RequestDelegate CreateUnimplementedService()
        {
            var logger = _loggerFactory.CreateLogger<ServerCallHandlerFactory<TService>>();

            return httpContext =>
            {
                // CORS preflight request should be handled by CORS middleware.
                // If it isn't then return 404 from endpoint request delegate.
                if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
                {
                    httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                    return Task.CompletedTask;
                }

                GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

                var unimplementedService = httpContext.Request.RouteValues["unimplementedService"]?.ToString() ?? "<unknown>";
                Log.ServiceUnimplemented(logger, unimplementedService);
                GrpcEventSource.Log.CallUnimplemented(httpContext.Request.Path.Value);

                GrpcProtocolHelpers.SetStatus(GrpcProtocolHelpers.GetTrailersDestination(httpContext.Response), new Status(StatusCode.Unimplemented, "Service is unimplemented."));
                return Task.CompletedTask;
            };
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _serviceUnimplemented =
                LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "ServiceUnimplemented"), "Service '{ServiceName}' is unimplemented.");

            private static readonly Action<ILogger, string, Exception?> _methodUnimplemented =
                LoggerMessage.Define<string>(LogLevel.Information, new EventId(2, "MethodUnimplemented"), "Method '{MethodName}' is unimplemented.");

            public static void ServiceUnimplemented(ILogger logger, string serviceName)
            {
                _serviceUnimplemented(logger, serviceName, null);
            }

            public static void MethodUnimplemented(ILogger logger, string methodName)
            {
                _methodUnimplemented(logger, methodName, null);
            }
        }
    }
}
