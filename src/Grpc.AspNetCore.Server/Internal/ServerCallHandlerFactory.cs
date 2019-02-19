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

using Grpc.AspNetCore.Server.Internal.CallHandlers;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class ServerCallHandlerFactory<TService> where TService : class
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
                SendMaxMessageSize = so.SendMaxMessageSize ?? go.SendMaxMessageSize
            };
        }

        public UnaryServerCallHandler<TRequest, TResponse, TService> CreateUnary<TRequest, TResponse>(Method<TRequest, TResponse> method)
            where TRequest : class
            where TResponse : class
        {
            return new UnaryServerCallHandler<TRequest, TResponse, TService>(method, _resolvedOptions, _loggerFactory);
        }

        public ClientStreamingServerCallHandler<TRequest, TResponse, TService> CreateClientStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method)
            where TRequest : class
            where TResponse : class
        {
            return new ClientStreamingServerCallHandler<TRequest, TResponse, TService>(method, _resolvedOptions, _loggerFactory);
        }

        public DuplexStreamingServerCallHandler<TRequest, TResponse, TService> CreateDuplexStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method)
            where TRequest : class
            where TResponse : class
        {
            return new DuplexStreamingServerCallHandler<TRequest, TResponse, TService>(method, _resolvedOptions, _loggerFactory);
        }

        public ServerStreamingServerCallHandler<TRequest, TResponse, TService> CreateServerStreaming<TRequest, TResponse>(Method<TRequest, TResponse> method)
            where TRequest : class
            where TResponse : class
        {
            return new ServerStreamingServerCallHandler<TRequest, TResponse, TService>(method, _resolvedOptions, _loggerFactory);
        }
    }
}
