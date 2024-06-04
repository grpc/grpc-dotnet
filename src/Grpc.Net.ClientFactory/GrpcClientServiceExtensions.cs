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
using System.Diagnostics.CodeAnalysis;
using Grpc.Net.ClientFactory;
using Grpc.Net.ClientFactory.Internal;
using Grpc.Shared;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

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
    public static IHttpClientBuilder AddGrpcClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services)
        where TClient : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);

        return services.AddGrpcClient<TClient>(o => { });
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
    public static IHttpClientBuilder AddGrpcClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, Action<GrpcClientFactoryOptions> configureClient)
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
    public static IHttpClientBuilder AddGrpcClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, Action<IServiceProvider, GrpcClientFactoryOptions> configureClient)
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
    public static IHttpClientBuilder AddGrpcClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, string name)
        where TClient : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(name);

        return services.AddGrpcClient<TClient>(name, o => { });
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
    public static IHttpClientBuilder AddGrpcClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, string name, Action<GrpcClientFactoryOptions> configureClient)
        where TClient : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(name);
        ArgumentNullThrowHelper.ThrowIfNull(configureClient);

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
    public static IHttpClientBuilder AddGrpcClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, string name, Action<IServiceProvider, GrpcClientFactoryOptions> configureClient)
        where TClient : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);
        ArgumentNullThrowHelper.ThrowIfNull(name);
        ArgumentNullThrowHelper.ThrowIfNull(configureClient);

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

    private static IHttpClientBuilder AddGrpcClientCore<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, string name) where TClient : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(name);

        // Transient so that IServiceProvider injected into constructor is for the current scope.
        services.TryAddTransient<GrpcClientFactory, DefaultGrpcClientFactory>();

        services.TryAddSingleton<GrpcCallInvokerFactory>();
        services.TryAddSingleton<DefaultClientActivator<TClient>>();

        // Registry is used to track state and report errors **DURING** service registration. This has to be an instance
        // because we access it by reaching into the service collection.
        services.TryAddSingleton(new GrpcClientMappingRegistry());

        return services.AddGrpcHttpClient<TClient>(name);
    }

    /// <summary>
    /// This is a custom method to register the HttpClient and typed factory. Needed because we need to access the config name when creating the typed client
    /// </summary>
    private static IHttpClientBuilder AddGrpcHttpClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(this IServiceCollection services, string name)
        where TClient : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);

        var builder = services.AddHttpClient(name);

        builder.Services.AddTransient<TClient>(s =>
        {
            var clientFactory = s.GetRequiredService<GrpcClientFactory>();
            return clientFactory.CreateClient<TClient>(name);
        });

        // Insert primary handler before other configuration so there is the opportunity to override it.
        // This should run before ConfigureDefaultHttpClient so the handler can be overriden in defaults.
        var configurePrimaryHandler = ServiceDescriptor.Singleton<IConfigureOptions<HttpClientFactoryOptions>>(new ConfigureNamedOptions<HttpClientFactoryOptions>(name, options =>
        {
            options.HttpMessageHandlerBuilderActions.Add(b =>
            {
                if (HttpHandlerFactory.TryCreatePrimaryHandler(out var handler))
                {
#if NET5_0_OR_GREATER
                    if (handler is SocketsHttpHandler socketsHttpHandler)
                    {
                        // A channel is created once per client, lives forever, and the primary handler never changes.
                        // It's possible that long lived connections cause the client to miss out on DNS changes.
                        // Replicate the core benefit of a handler lifetime (periodic connection recreation)
                        // by setting PooledConnectionLifetime to handler lifetime.
                        socketsHttpHandler.PooledConnectionLifetime = options.HandlerLifetime;
                    }
#endif

                    b.PrimaryHandler = handler;
                }
                else
                {
                    b.PrimaryHandler = UnsupportedHttpHandler.Instance;
                }
            });
        }));
        services.Insert(0, configurePrimaryHandler);

        // Some platforms don't have a built-in handler that supports gRPC.
        // Validate that a handler was set by the app to after all configuration has run.
        services.PostConfigure<HttpClientFactoryOptions>(name, options =>
        {
            options.HttpMessageHandlerBuilderActions.Add(builder =>
            {
                if (builder.PrimaryHandler == UnsupportedHttpHandler.Instance)
                {
                    throw HttpHandlerFactory.CreateUnsupportedHandlerException();
                }
            });
        });

        ReserveClient(builder, typeof(TClient), name);

        return builder;
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

    private sealed class UnsupportedHttpHandler : HttpMessageHandler
    {
        public static readonly UnsupportedHttpHandler Instance = new UnsupportedHttpHandler();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(HttpHandlerFactory.CreateUnsupportedHandlerException());
        }
    }
}
