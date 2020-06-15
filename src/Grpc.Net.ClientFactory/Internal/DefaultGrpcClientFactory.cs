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
using System.Threading;
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
        private readonly IOptionsMonitor<GrpcClientFactoryRegistration> _clientFactoryOptionsMonitor;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILoggerFactory _loggerFactory;

        public DefaultGrpcClientFactory(IServiceProvider serviceProvider,
            IOptionsMonitor<GrpcClientFactoryRegistration> clientFactoryOptionsMonitor,
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

            var builder = new GrpcClientFactoryOptions(_serviceProvider, name);

            // Set options defaults first in case actions want to modify them
            builder.ChannelOptions.HttpClient = _httpClientFactory.CreateClient(name);
            builder.ChannelOptions.LoggerFactory = _loggerFactory;

            // Long running server and duplex streaming gRPC requests may not
            // return any messages for over 100 seconds, triggering a cancellation
            // of HttpClient.SendAsync. Disable timeout in internally created
            // HttpClient for channel.
            //
            // gRPC deadline should be the recommended way to timeout gRPC calls.
            //
            // https://github.com/dotnet/corefx/issues/41650
            builder.ChannelOptions.HttpClient.Timeout = Timeout.InfiniteTimeSpan;

            for (int i = 0; i < clientFactoryOptions.GrpcClientFactoryOptionsActions.Count; i++)
            {
                clientFactoryOptions.GrpcClientFactoryOptionsActions[i](builder);
            }

            return Build<TClient>(builder);
        }

        private TClient Build<TClient>(GrpcClientFactoryOptions builder) where TClient : class
        {
            var defaultClientActivator = builder.Services.GetService<DefaultClientActivator<TClient>>();
            if (defaultClientActivator == null)
            {
                throw new InvalidOperationException($"No gRPC client configured with name '{builder.Name}'.");
            }

            var callInvoker = CreateCallInvoker(builder);

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

        public CallInvoker CreateCallInvoker(GrpcClientFactoryOptions builder)
        {
            var resolvedAddress = builder.Address ?? builder.ChannelOptions.HttpClient?.BaseAddress;
            if (resolvedAddress == null)
            {
                throw new InvalidOperationException($"Could not resolve the address for gRPC client '{builder.Name}'.");
            }

#pragma warning disable CS0612, CS0618 // Type or member is obsolete
            for (int i = 0; i < builder.ChannelOptionsActions.Count; i++)
            {
                builder.ChannelOptionsActions[i](builder.ChannelOptions);
            }
#pragma warning restore CS0612, CS0618 // Type or member is obsolete

            var channel = GrpcChannel.ForAddress(resolvedAddress, builder.ChannelOptions);

            var httpClientCallInvoker = channel.CreateCallInvoker();

            var resolvedCallInvoker = builder.Interceptors.Count > 0
                ? httpClientCallInvoker.Intercept(builder.Interceptors.ToArray())
                : httpClientCallInvoker;

            return resolvedCallInvoker;
        }
    }
}
