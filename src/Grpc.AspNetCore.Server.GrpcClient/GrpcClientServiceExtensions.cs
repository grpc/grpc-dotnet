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
using Grpc.AspNetCore.Server.GrpcClient;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GrpcClientServiceExtensions
    {
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, Action<GrpcClientOptions<TClient>> configureClient) where TClient : ClientBase<TClient>
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configureClient == null)
            {
                throw new ArgumentNullException(nameof(configureClient));
            }

            services.TryAdd(ServiceDescriptor.Transient(typeof(ITypedHttpClientFactory<TClient>), typeof(GrpcHttpClientFactory<TClient>)));
            services.TryAdd(ServiceDescriptor.Transient(typeof(GrpcHttpClientFactory<TClient>.Cache), typeof(GrpcHttpClientFactory<TClient>.Cache)));

            services.Configure(configureClient);

            // Accessing the client options here allows the HttpContextAccessor to be added as needed
            // Is there a way to get the value from services.Configure now?
            var clientOptions = new GrpcClientOptions<TClient>();
            configureClient(clientOptions);

            // HttpContextAccessor has performance overhead. Only add it when required
            if (RequireHttpContextAccessor(clientOptions))
            {
                services.AddHttpContextAccessor();
            }

            var clientBuilder = services.AddHttpClient<TClient>(httpClient =>
            {
                httpClient.BaseAddress = clientOptions.BaseAddress;
            });

            if (clientOptions.Certificate != null)
            {
                clientBuilder.ConfigurePrimaryHttpMessageHandler(() =>
                {
                    var handler = new HttpClientHandler();
                    handler.ClientCertificates.Add(clientOptions.Certificate);

                    return handler;
                });
            }

            return clientBuilder;
        }

        private static bool RequireHttpContextAccessor<TClient>(GrpcClientOptions<TClient> clientOptions) where TClient : ClientBase<TClient>
        {
            return clientOptions.UseRequestCancellationToken || clientOptions.UseRequestDeadline;
        }
    }
}
