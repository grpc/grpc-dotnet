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
using System.Linq;
using System.Net.Http;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.Net.ClientFactory.Internal
{
    internal class DefaultGrpcClientFactory : GrpcClientFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IOptionsMonitor<GrpcClientFactoryOptions> _clientFactoryOptionsMonitor;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILoggerFactory _loggerFactory;

        public DefaultGrpcClientFactory(IServiceProvider serviceProvider,
            IOptionsMonitor<GrpcClientFactoryOptions> clientFactoryOptionsMonitor,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory)
        {
            _serviceProvider = serviceProvider;
            _clientFactoryOptionsMonitor = clientFactoryOptionsMonitor;
            _httpClientFactory = httpClientFactory;
            _loggerFactory = loggerFactory;
        }

        public override TClient CreateClient<TClient>(string name) where TClient : class
        {
            var clientFactoryOptions = _clientFactoryOptionsMonitor.Get(name);

            var builder = new GrpcClientBuilder(_serviceProvider, name);

            // Set options defaults first in case actions want to modify them
            builder.ChannelOptions.HttpClient = _httpClientFactory.CreateClient(name);
            builder.ChannelOptions.LoggerFactory = _loggerFactory;

            for (int i = 0; i < clientFactoryOptions.ClientBuilderActions.Count; i++)
            {
                clientFactoryOptions.ClientBuilderActions[i](builder);
            }

            ApplyClientFactoryOptionsSettings(clientFactoryOptions, builder);

            return Build<TClient>(builder);
        }

        private TClient Build<TClient>(GrpcClientBuilder builder) where TClient : class
        {
            var defaultClientActivator = builder.Services.GetService<DefaultClientActivator<TClient>>();
            if (defaultClientActivator == null)
            {
                throw new InvalidOperationException($"No gRPC client configured with name '{builder.Name}'.");
            }

            var callInvoker = CreateCallInvoker(builder, null!);

            if (builder.Creator != null)
            {
                var c = builder.Creator(callInvoker);
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

        public CallInvoker CreateCallInvoker(GrpcClientBuilder builder, Uri address)
        {
            var resolvedAddress = address ?? builder.ChannelOptions.HttpClient?.BaseAddress;
            if (resolvedAddress == null)
            {
                throw new InvalidOperationException($"Could not resolve the address for gRPC client '{builder.Name}'.");
            }

            var channel = GrpcChannel.ForAddress(resolvedAddress, builder.ChannelOptions);

            var httpClientCallInvoker = channel.CreateCallInvoker();

            var resolvedCallInvoker = builder.Interceptors.Count > 0
                ? httpClientCallInvoker.Intercept(builder.Interceptors.ToArray())
                : httpClientCallInvoker;

            return resolvedCallInvoker;
        }

        private static void ApplyClientFactoryOptionsSettings(GrpcClientFactoryOptions clientFactoryOptions, GrpcClientBuilder builder)
        {
            if (clientFactoryOptions.ChannelOptionsActions.Count > 0)
            {
                foreach (var applyOptions in clientFactoryOptions.ChannelOptionsActions)
                {
                    applyOptions(builder.ChannelOptions);
                }
            }

            for (var i = 0; i < clientFactoryOptions.Interceptors.Count; i++)
            {
                builder.Interceptors.Add(clientFactoryOptions.Interceptors[i]);
            }

            if (clientFactoryOptions.Creator != null)
            {
                if (builder.Creator != null)
                {
                    throw new InvalidOperationException($"Client creators have been set on both {nameof(GrpcClientFactoryOptions)} and {nameof(GrpcClientBuilder)}.");
                }

                builder.Creator = clientFactoryOptions.Creator;
            }
        }
    }
}
