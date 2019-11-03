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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Grpc.Net.Compression;
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
        private readonly GrpcServiceOptions _globalOptions;
        private readonly GrpcServiceOptions<TService> _serviceOptions;
        private readonly IGrpcServiceActivator<TService> _serviceActivator;
        private readonly IServiceProvider _serviceProvider;

        public ServerCallHandlerFactory(
            ILoggerFactory loggerFactory,
            IOptions<GrpcServiceOptions> globalOptions,
            IOptions<GrpcServiceOptions<TService>> serviceOptions,
            IGrpcServiceActivator<TService> serviceActivator,
            IServiceProvider serviceProvider)
        {
            _loggerFactory = loggerFactory;
            _serviceOptions = serviceOptions.Value;
            _globalOptions = globalOptions.Value;
            _serviceActivator = serviceActivator;
            _serviceProvider = serviceProvider;
        }

        private MethodContext CreateMethodContext<TRequest, TResponse>()
        {
            // This is required to get ensure that service methods without any explicit configuration
            // will continue to get the global configuration options
            var resolvedCompressionProviders = new Dictionary<string, ICompressionProvider>(StringComparer.Ordinal);
            AddCompressionProviders(resolvedCompressionProviders, _serviceOptions._compressionProviders);
            AddCompressionProviders(resolvedCompressionProviders, _globalOptions._compressionProviders);

            var interceptors = new InterceptorCollection();
            interceptors.AddRange(_globalOptions.Interceptors);
            interceptors.AddRange(_serviceOptions.Interceptors);

            return new MethodContext
            (
                requestType: typeof(TRequest),
                responseType: typeof(TResponse),
                compressionProviders: resolvedCompressionProviders,
                interceptors: interceptors,
                maxSendMessageSize: _serviceOptions.MaxSendMessageSize ?? _globalOptions.MaxSendMessageSize,
                maxReceiveMessageSize: _serviceOptions.MaxReceiveMessageSize ?? _globalOptions.MaxReceiveMessageSize,
                enableDetailedErrors: _serviceOptions.EnableDetailedErrors ?? _globalOptions.EnableDetailedErrors,
                responseCompressionAlgorithm: _serviceOptions.ResponseCompressionAlgorithm ?? _globalOptions.ResponseCompressionAlgorithm,
                responseCompressionLevel: _serviceOptions.ResponseCompressionLevel ?? _globalOptions.ResponseCompressionLevel
            );
        }

        private static void AddCompressionProviders(Dictionary<string, ICompressionProvider> resolvedProviders, IList<ICompressionProvider>? compressionProviders)
        {
            if (compressionProviders != null)
            {
                foreach (var compressionProvider in compressionProviders)
                {
                    if (!resolvedProviders.ContainsKey(compressionProvider.EncodingName))
                    {
                        resolvedProviders.Add(compressionProvider.EncodingName, compressionProvider);
                    }
                }
            }
        }

        public UnaryServerCallHandler<TService, TRequest, TResponse> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new UnaryServerCallHandler<TService, TRequest, TResponse>(method, invoker, CreateMethodContext<TRequest, TResponse>(), _loggerFactory, _serviceActivator, _serviceProvider);
        }

        public ClientStreamingServerCallHandler<TService, TRequest, TResponse> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new ClientStreamingServerCallHandler<TService, TRequest, TResponse>(method, invoker, CreateMethodContext<TRequest, TResponse>(), _loggerFactory, _serviceActivator, _serviceProvider);
        }

        public DuplexStreamingServerCallHandler<TService, TRequest, TResponse> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new DuplexStreamingServerCallHandler<TService, TRequest, TResponse>(method, invoker, CreateMethodContext<TRequest, TResponse>(), _loggerFactory, _serviceActivator, _serviceProvider);
        }

        public ServerStreamingServerCallHandler<TService, TRequest, TResponse> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new ServerStreamingServerCallHandler<TService, TRequest, TResponse>(method, invoker, CreateMethodContext<TRequest, TResponse>(), _loggerFactory, _serviceActivator, _serviceProvider);
        }

        public RequestDelegate CreateUnimplementedMethod()
        {
            var logger = _loggerFactory.CreateLogger<ServerCallHandlerFactory<TService>>();

            return httpContext =>
            {
                GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

                var unimplementedMethod = httpContext.Request.RouteValues["unimplementedMethod"]?.ToString() ?? "<unknown>";
                Log.MethodUnimplemented(logger, unimplementedMethod);
                GrpcEventSource.Log.CallUnimplemented(httpContext.Request.Path.Value);

                GrpcProtocolHelpers.SetStatus(GrpcProtocolHelpers.GetTrailersDestination(httpContext.Response), new Status(StatusCode.Unimplemented, "Method is unimplemented."));
                return Task.CompletedTask;
            };
        }

        public RequestDelegate CreateUnimplementedService()
        {
            var logger = _loggerFactory.CreateLogger<ServerCallHandlerFactory<TService>>();

            return httpContext =>
            {
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
