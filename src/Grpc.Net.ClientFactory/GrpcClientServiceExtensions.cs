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
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, Action<GrpcClientFactoryOptions> configureClient)
            where TClient : ClientBase
        {
            var name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);

            return services.AddGrpcClient<TClient>(name, configureClient);
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
        /// <para>
        /// The <see cref="IServiceProvider"/> argument provided to <paramref name="configureClient"/> will be
        /// a reference to a scoped service provider that shares the lifetime of the handler being constructed.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, Action<IServiceProvider, GrpcClientFactoryOptions> configureClient)
            where TClient : ClientBase
        {
            var name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);

            return services.AddGrpcClient<TClient>(name, configureClient);
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
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, string name, Action<GrpcClientFactoryOptions> configureClient)
            where TClient : ClientBase
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (configureClient == null)
            {
                throw new ArgumentNullException(nameof(configureClient));
            }

            services.Configure(name, configureClient);

            return services.AddGrpcClientCore<TClient>(name);
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
        /// <para>
        /// The <see cref="IServiceProvider"/> argument provided to <paramref name="configureClient"/> will be
        /// a reference to a scoped service provider that shares the lifetime of the handler being constructed.
        /// </para>
        /// </remarks>
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, string name, Action<IServiceProvider, GrpcClientFactoryOptions> configureClient)
            where TClient : ClientBase
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (configureClient == null)
            {
                throw new ArgumentNullException(nameof(configureClient));
            }

            services.AddTransient<IConfigureOptions<GrpcClientFactoryOptions>>(services =>
            {
                return new ConfigureNamedOptions<GrpcClientFactoryOptions>(name, (options) =>
                {
                    configureClient(services, options);
                });
            });

            return services.AddGrpcClientCore<TClient>(name);
        }

        private static IHttpClientBuilder AddGrpcClientCore<TClient>(this IServiceCollection services, string name) where TClient : ClientBase
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            services.TryAddSingleton<GrpcClientFactory, DefaultGrpcClientFactory>();

            services.TryAdd(ServiceDescriptor.Transient(typeof(INamedTypedHttpClientFactory<TClient>), typeof(GrpcHttpClientFactory<TClient>)));
            services.TryAdd(ServiceDescriptor.Transient(typeof(GrpcHttpClientFactory<TClient>.Cache), typeof(GrpcHttpClientFactory<TClient>.Cache)));

            Action<IServiceProvider, HttpClient> configureTypedClient = (s, httpClient) =>
            {
                var os = s.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
                var clientOptions = os.Get(name);

                httpClient.BaseAddress = clientOptions.BaseAddress;
            };

            services.Configure<GrpcClientFactoryOptions>(name, options => options.ExplicitlySet = true);

            IHttpClientBuilder clientBuilder = services.AddGrpcHttpClient<TClient>(name, configureTypedClient);

            return clientBuilder;
        }

        /// <summary>
        /// This is a custom method to register the HttpClient and typed factory. Needed because we need to access the config name when creating the typed client
        /// </summary>
        private static IHttpClientBuilder AddGrpcHttpClient<TClient>(this IServiceCollection services, string name, Action<IServiceProvider, HttpClient> configureClient)
            where TClient : ClientBase
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddHttpClient();

            var builder = new DefaultHttpClientBuilder(services, name);
            builder.ConfigureHttpClient(configureClient);

            builder.Services.AddTransient<TClient>(s =>
            {
                var httpClientFactory = s.GetRequiredService<IHttpClientFactory>();
                var httpClient = httpClientFactory.CreateClient(builder.Name);

                var typedClientFactory = s.GetRequiredService<INamedTypedHttpClientFactory<TClient>>();
                return typedClientFactory.CreateClient(httpClient, builder.Name);
            });

            return builder;
        }

        private class DefaultHttpClientBuilder : IHttpClientBuilder
        {
            public DefaultHttpClientBuilder(IServiceCollection services, string name)
            {
                Services = services;
                Name = name;
            }

            public string Name { get; }

            public IServiceCollection Services { get; }
        }
    }
}
