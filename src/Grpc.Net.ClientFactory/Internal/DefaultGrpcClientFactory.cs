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
using System.Net.Http;
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

            var clientFactoryOptions = _grpcClientFactoryOptionsMonitor.Get(name);
            var httpHandler = _messageHandlerFactory.CreateHandler(name);
            var callInvoker = _callInvokerFactory.CreateCallInvoker(httpHandler, name, typeof(TClient), clientFactoryOptions);

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
    }
}
