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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extensions methods to configure an <see cref="IServiceCollection"/> for <see cref="IHttpClientFactory"/> with gRPC.
    /// </summary>
    public static class GrpcClientServiceExtensions
    {
        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>. The client name
        /// will be set to the full name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type must inherit from <see cref="ClientBase{TClient}"/>. The type specified will be registered in the service collection as
        /// a transient service.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="configureClient">A delegate that is used to configure the gRPC client.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using 
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type. 
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, Action<GrpcClientOptions<TClient>> configureClient) where TClient : ClientBase<TClient>
        {
            return services.AddGrpcClientCore(name: null, configureClient);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>. The client name
        /// will be set to the full name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the typed client. The type must inherit from <see cref="ClientBase{TClient}"/>. The type specified will be registered in the service collection as
        /// a transient service.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
        /// <param name="configureClient">A delegate that is used to configure the gRPC client.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        /// <remarks>
        /// <para>
        /// <see cref="HttpClient"/> instances that apply the provided configuration can be retrieved using 
        /// <see cref="IHttpClientFactory.CreateClient(string)"/> and providing the matching name.
        /// </para>
        /// <para>
        /// <typeparamref name="TClient"/> instances constructed with the appropriate <see cref="HttpClient" />
        /// can be retrieved from <see cref="IServiceProvider.GetService(Type)" /> (and related methods) by providing
        /// <typeparamref name="TClient"/> as the service type. 
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, string name, Action<GrpcClientOptions<TClient>> configureClient) where TClient : ClientBase<TClient>
        {
            return services.AddGrpcClientCore(name, configureClient);
        }

        private static IHttpClientBuilder AddGrpcClientCore<TClient>(this IServiceCollection services, string name, Action<GrpcClientOptions<TClient>> configureClient) where TClient : ClientBase<TClient>
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (configureClient == null)
            {
                throw new ArgumentNullException(nameof(configureClient));
            }

            // HttpContextAccessor is used to resolve the cancellation token, deadline and other request details to use with nested gRPC requests
            services.AddHttpContextAccessor();

            services.TryAdd(ServiceDescriptor.Transient(typeof(ITypedHttpClientFactory<TClient>), typeof(GrpcHttpClientFactory<TClient>)));
            services.TryAdd(ServiceDescriptor.Transient(typeof(GrpcHttpClientFactory<TClient>.Cache), typeof(GrpcHttpClientFactory<TClient>.Cache)));

            services.Configure(configureClient);

            Action<IServiceProvider, HttpClient> configureTypedClient = (s, httpClient) =>
            {
                var clientOptions = s.GetRequiredService<IOptions<GrpcClientOptions<TClient>>>().Value;

                httpClient.BaseAddress = clientOptions.BaseAddress;
            };

            var clientBuilder = (name == null)
                ? services.AddHttpClient<TClient>(configureTypedClient)
                : services.AddHttpClient<TClient>(name, configureTypedClient);

            clientBuilder.ConfigurePrimaryHttpMessageHandler(s =>
            {
                var clientOptions = s.GetRequiredService<IOptions<GrpcClientOptions<TClient>>>().Value;

                var handler = new HttpClientHandler();
                if (clientOptions.Certificate != null)
                {
                    handler.ClientCertificates.Add(clientOptions.Certificate);
                }

                return handler;
            });

            return clientBuilder;
        }

        private static bool RequireHttpContextAccessor<TClient>(GrpcClientOptions<TClient> clientOptions) where TClient : ClientBase<TClient>
        {
            return clientOptions.UseRequestCancellationToken || clientOptions.UseRequestDeadline;
        }
    }
}
