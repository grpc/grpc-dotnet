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
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Grpc.Net.ClientFactory.Internal
{
    internal class DefaultGrpcClientFactory : GrpcClientFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly GrpcCallInvokerFactory _callInvokerFactory;
        private readonly IOptionsMonitor<GrpcClientFactoryOptions> _grpcClientFactoryOptionsMonitor;
        private readonly IOptionsMonitor<HttpClientFactoryOptions> _httpClientFactoryOptionsMonitor;
        private readonly IHttpMessageHandlerFactory _messageHandlerFactory;

        public DefaultGrpcClientFactory(IServiceProvider serviceProvider,
            GrpcCallInvokerFactory callInvokerFactory,
            IOptionsMonitor<GrpcClientFactoryOptions> grpcClientFactoryOptionsMonitor,
            IOptionsMonitor<HttpClientFactoryOptions> httpClientFactoryOptionsMonitor,
            IHttpMessageHandlerFactory messageHandlerFactory)
        {
            _serviceProvider = serviceProvider;
            _callInvokerFactory = callInvokerFactory;
            _grpcClientFactoryOptionsMonitor = grpcClientFactoryOptionsMonitor;
            _httpClientFactoryOptionsMonitor = httpClientFactoryOptionsMonitor;
            _messageHandlerFactory = messageHandlerFactory;
        }

        public override TClient CreateClient<TClient>(string name) where TClient : class
        {
            var defaultClientActivator = _serviceProvider.GetService<DefaultClientActivator<TClient>>();
            if (defaultClientActivator == null)
            {
                throw new InvalidOperationException($"No gRPC client configured with name '{name}'.");
            }

            var httpClientFactoryOptions = _httpClientFactoryOptionsMonitor.Get(name);
            if (httpClientFactoryOptions.HttpClientActions.Count > 0)
            {
                throw new InvalidOperationException($"The ConfigureHttpClient method is not supported when creating gRPC clients. Unable to create client with name '{name}'.");
            }

            var httpHandler = _messageHandlerFactory.CreateHandler(name);

            var clientFactoryOptions = _grpcClientFactoryOptionsMonitor.Get(name);

            var callInvoker = GetOrCreateCallInvoker<TClient>(name, httpHandler, clientFactoryOptions);

            if (clientFactoryOptions.Creator != null)
            {
                var c = clientFactoryOptions.Creator(callInvoker);
                if (c is TClient client)
                {
                    return client;
                }
                else if (c == null)
                {
                    throw new InvalidOperationException("A null instance was returned by the configured client creator.");
                }

                throw new InvalidOperationException($"The {c.GetType().FullName} instance returned by the configured client creator is not compatible with {typeof(TClient).FullName}.");
            }
            else
            {
                return defaultClientActivator.CreateClient(callInvoker);
            }
        }

        private CallInvoker GetOrCreateCallInvoker<TClient>(string name, HttpMessageHandler httpHandler, GrpcClientFactoryOptions clientFactoryOptions) where TClient : class
        {
            // Buckle up because creating the channel and invoker is a bit of a hack. The goal here is to
            // have a channel with the same DI scope and lifetime as the handlers.
            //
            // To do this the DefaultGrpcClientFactoryHandler is added to the handler chain. This handler
            // stores the service provider for the handler scope, and it caches the channel. When the handler
            // is disposed it will dispose the channel.
            //
            // Logic:
            // 1. The handler is gotten from the handler chain.
            // 2. If the handler hasn't been initialized then create the channel using the scoped service provider.
            // 3. Return the channel.
            var factoryHandler = GetHttpHandlerType<DefaultGrpcClientFactoryHandler>(httpHandler);
            if (factoryHandler == null)
            {
                throw new InvalidOperationException($"No gRPC client configured with name '{name}'."); ;
            }

            if (!factoryHandler.IsInitialized)
            {
                lock (factoryHandler)
                {
                    if (!factoryHandler.IsInitialized)
                    {
                        var result = _callInvokerFactory.CreateCallInvoker(httpHandler, name, typeof(TClient), clientFactoryOptions);

                        factoryHandler.Channel = result.Channel;
                        factoryHandler.Invoker = result.Invoker;
                        factoryHandler.IsInitialized = true;
                    }
                }
            }
            
            return factoryHandler.Invoker;
        }

        private static T? GetHttpHandlerType<T>(HttpMessageHandler handler) where T : DelegatingHandler
        {
            if (handler is T match)
            {
                return match;
            }

            HttpMessageHandler? currentHandler = handler;
            while (currentHandler is DelegatingHandler delegatingHandler)
            {
                currentHandler = delegatingHandler.InnerHandler;

                if (currentHandler is T m)
                {
                    return m;
                }
            }

            return null;
        }
    }
}
