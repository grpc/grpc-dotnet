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
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Shared;
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
        /// will be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the gRPC client. The type specified will be registered in the service collection as
        /// a transient service.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
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
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services)
            where TClient : class
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            var name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);

            return services.AddGrpcClientCore<TClient>(name);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>. The client name
        /// will be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the gRPC client. The type specified will be registered in the service collection as
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
            where TClient : class
        {
            var name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);

            return services.AddGrpcClient<TClient>(name, configureClient);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>. The client name
        /// will be set to the type name of <typeparamref name="TClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the gRPC client. The type specified will be registered in the service collection as
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
            where TClient : class
        {
            var name = TypeNameHelper.GetTypeDisplayName(typeof(TClient), fullName: false);

            return services.AddGrpcClient<TClient>(name, configureClient);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the gRPC client. The type specified will be registered in the service collection as
        /// a transient service.
        /// </typeparam>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <param name="name">The logical name of the <see cref="HttpClient"/> to configure.</param>
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
        public static IHttpClientBuilder AddGrpcClient<TClient>(this IServiceCollection services, string name)
            where TClient : class
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return services.AddGrpcClientCore<TClient>(name);
        }

        /// <summary>
        /// Adds the <see cref="IHttpClientFactory"/> and related services to the <see cref="IServiceCollection"/> and configures
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the gRPC client. The type specified will be registered in the service collection as
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
            where TClient : class
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
        /// a binding between the <typeparamref name="TClient"/> type and a named <see cref="HttpClient"/>.
        /// </summary>
        /// <typeparam name="TClient">
        /// The type of the gRPC client. The type specified will be registered in the service collection as
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
            where TClient : class
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
                return new ConfigureNamedOptions<GrpcClientFactoryOptions>(name, options =>
                {
                    configureClient(services, options);
                });
            });

            // `IConfigureOptions<GrpcClientFactoryOptions>` presence in builder's ServicesCollection is tested
            // in gRPC client extension methods that take IHttpClientBuilder. Validation will throw an error if
            // if gRPC extension methods, e.g. AddInterceptor, are used with client builders that are not from
            // AddGrpcClient. ConfigureNamedOptions<GrpcClientFactoryOptions> needs to be the value.
            // We need to cast the service value to the concrete type to get the name.
            // Needed here because config options registered here are transient.
            services.AddSingleton<IConfigureOptions<GrpcClientFactoryOptions>>(
                new ConfigureNamedOptions<GrpcClientFactoryOptions>(name, options => { }));

            return services.AddGrpcClientCore<TClient>(name);
        }

        private static IHttpClientBuilder AddGrpcClientCore<TClient>(this IServiceCollection services, string name) where TClient : class
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            services.TryAddSingleton<GrpcClientFactory, DefaultGrpcClientFactory>();

            services.TryAddSingleton<GrpcCallInvokerFactory>();
            services.TryAddSingleton<DefaultClientActivator<TClient>>();

            // Registry is used to track state and report errors **DURING** service registration. This has to be an instance
            // because we access it by reaching into the service collection.
            services.TryAddSingleton(new GrpcClientMappingRegistry());

            IHttpClientBuilder clientBuilder = services.AddGrpcHttpClient<TClient>(name);

            return clientBuilder;
        }

        /// <summary>
        /// This is a custom method to register the HttpClient and typed factory. Needed because we need to access the config name when creating the typed client
        /// </summary>
        private static IHttpClientBuilder AddGrpcHttpClient<TClient>(this IServiceCollection services, string name)
            where TClient : class
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services
                .AddHttpClient(name)
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    // Set PrimaryHandler to null so we can track whether the user
                    // set a value or not. If they didn't set their own handler then
                    // one will be created by PostConfigure.
                    return null!;
                });

            services.PostConfigure<HttpClientFactoryOptions>(name, options =>
            {
                options.HttpMessageHandlerBuilderActions.Add(static builder =>
                {
                    if (builder.PrimaryHandler == null)
                    {
                        // This will throw in .NET Standard 2.0 with a prompt that a user must set a handler.
                        // Because it throws it should only be called in PostConfigure if no handler has been set.
                        var handler = HttpHandlerFactory.CreatePrimaryHandler();
#if NET5_0
                        handler = HttpHandlerFactory.EnsureTelemetryHandler(handler);
#endif

                        builder.PrimaryHandler = handler;
                    }
                });
            });


            var builder = new DefaultHttpClientBuilder(services, name);

            builder.Services.AddTransient<TClient>(s =>
            {
                var clientFactory = s.GetRequiredService<GrpcClientFactory>();
                return clientFactory.CreateClient<TClient>(builder.Name);
            });

            ReserveClient(builder, typeof(TClient), name);

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

        private static void ReserveClient(IHttpClientBuilder builder, Type type, string name)
        {
            var registry = (GrpcClientMappingRegistry?)builder.Services.Single(sd => sd.ServiceType == typeof(GrpcClientMappingRegistry)).ImplementationInstance;
            CompatibilityHelpers.Assert(registry != null);

            // Check for same name registered to two different types. This won't work because we rely on named options for the configuration.
            if (registry.NamedClientRegistrations.TryGetValue(name, out var otherType) && type != otherType)
            {
                var message =
                    $"The gRPC client factory already has a registered client with the name '{name}', bound to the type '{otherType.FullName}'. " +
                    $"Client names are computed based on the type name without considering the namespace ('{otherType.Name}'). " +
                    $"Use an overload of AddGrpcClient that accepts a string and provide a unique name to resolve the conflict.";
                throw new InvalidOperationException(message);
            }

            registry.NamedClientRegistrations[name] = type;
        }
    }
}
