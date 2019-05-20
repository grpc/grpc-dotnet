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
using System.Linq;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Compression;
using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.Core;
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
        private readonly GrpcServiceOptions _resolvedOptions;

        public ServerCallHandlerFactory(ILoggerFactory loggerFactory, IOptions<GrpcServiceOptions> globalOptions, IOptions<GrpcServiceOptions<TService>> serviceOptions)
        {
            _loggerFactory = loggerFactory;

            var so = serviceOptions.Value;
            var go = globalOptions.Value;

            // This is required to get ensure that service methods without any explicit configuration
            // will continue to get the global configuration options
            _resolvedOptions = new GrpcServiceOptions
            {
                EnableDetailedErrors = so.EnableDetailedErrors ?? go.EnableDetailedErrors,
                ReceiveMaxMessageSize = so.ReceiveMaxMessageSize ?? go.ReceiveMaxMessageSize,
                SendMaxMessageSize = so.SendMaxMessageSize ?? go.SendMaxMessageSize,
                ResponseCompressionAlgorithm = so.ResponseCompressionAlgorithm ?? go.ResponseCompressionAlgorithm,
                ResponseCompressionLevel = so.ResponseCompressionLevel ?? go.ResponseCompressionLevel,
                CompressionProviders = so._compressionProviders ?? go._compressionProviders ?? new List<ICompressionProvider>()
            };

            _resolvedOptions.Interceptors.AddRange(go.Interceptors);
            _resolvedOptions.Interceptors.AddRange(so.Interceptors);

            if (_resolvedOptions.ResponseCompressionAlgorithm != null)
            {
                var responseCompressionProvider = _resolvedOptions.CompressionProviders?.FirstOrDefault(p => string.Equals(_resolvedOptions.ResponseCompressionAlgorithm, p.EncodingName, StringComparison.Ordinal));
                if (responseCompressionProvider == null)
                {
                    throw new InvalidOperationException($"The configured response compression algorithm '{_resolvedOptions.ResponseCompressionAlgorithm}' does not have a matching compression provider.");
                }
            }
        }

        public UnaryServerCallHandler<TService, TRequest, TResponse> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new UnaryServerCallHandler<TService, TRequest, TResponse>(method, invoker, _resolvedOptions, _loggerFactory);
        }

        public ClientStreamingServerCallHandler<TService, TRequest, TResponse> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new ClientStreamingServerCallHandler<TService, TRequest, TResponse>(method, invoker, _resolvedOptions, _loggerFactory);
        }

        public DuplexStreamingServerCallHandler<TService, TRequest, TResponse> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new DuplexStreamingServerCallHandler<TService, TRequest, TResponse>(method, invoker, _resolvedOptions, _loggerFactory);
        }

        public ServerStreamingServerCallHandler<TService, TRequest, TResponse> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TService, TRequest, TResponse> invoker)
            where TRequest : class
            where TResponse : class
        {
            return new ServerStreamingServerCallHandler<TService, TRequest, TResponse>(method, invoker, _resolvedOptions, _loggerFactory);
        }

        public RequestDelegate CreateUnimplementedMethod()
        {
            var logger = _loggerFactory.CreateLogger<ServerCallHandlerFactory<TService>>();

            return httpContext =>
            {
                GrpcProtocolHelpers.AddProtocolHeaders(httpContext.Response);

                var unimplementedMethod = httpContext.Request.RouteValues["unimplementedMethod"]?.ToString() ?? "<unknown>";
                Log.MethodUnimplemented(logger, unimplementedMethod);

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
