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

using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
using Grpc.Net.ClientFactory;
using Grpc.Shared;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring an <see cref="IHttpClientBuilder"/>.
/// </summary>
public static class GrpcHttpClientBuilderExtensions
{
    /// <summary>
    /// Adds a delegate that will be used to configure the channel for a gRPC client.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureChannel">A delegate that is used to configure a <see cref="GrpcChannelOptions"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder ConfigureChannel(this IHttpClientBuilder builder, Action<IServiceProvider, GrpcChannelOptions> configureChannel)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureChannel);

        ValidateGrpcClient(builder, nameof(ConfigureChannel));

        builder.Services.AddTransient<IConfigureOptions<GrpcClientFactoryOptions>>(services =>
        {
            return new ConfigureNamedOptions<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.ChannelOptionsActions.Add(o => configureChannel(services, o));
            });
        });

        return builder;
    }

    /// <summary>
    /// Adds a delegate that will be used to configure the channel for a gRPC client.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureChannel">A delegate that is used to configure a <see cref="GrpcChannelOptions"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder ConfigureChannel(this IHttpClientBuilder builder, Action<GrpcChannelOptions> configureChannel)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureChannel);

        ValidateGrpcClient(builder, nameof(ConfigureChannel));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.ChannelOptionsActions.Add(configureChannel);
        });

        return builder;
    }

    /// <summary>
    /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
    /// The interceptor scope is <see cref="InterceptorScope.Channel"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, Func<IServiceProvider, Interceptor> configureInvoker)
    {
        return builder.AddInterceptor(InterceptorScope.Channel, configureInvoker);
    }

    /// <summary>
    /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="scope">The scope of the interceptor.</param>
    /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, InterceptorScope scope, Func<IServiceProvider, Interceptor> configureInvoker)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureInvoker);

        ValidateGrpcClient(builder, nameof(AddInterceptor));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.InterceptorRegistrations.Add(new InterceptorRegistration(scope, configureInvoker));
        });

        return builder;
    }

    /// <summary>
    /// Adds a delegate that will be used to create <see cref="CallCredentials"/> for a gRPC call.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="authInterceptor">A delegate that is used to create <see cref="CallCredentials"/> for a gRPC call.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddCallCredentials(this IHttpClientBuilder builder, Func<AuthInterceptorContext, Metadata, Task> authInterceptor)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(authInterceptor);

        ValidateGrpcClient(builder, nameof(AddCallCredentials));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.HasCallCredentials = true;
            options.CallOptionsActions.Add((callOptionsContext) =>
            {
                var credentials = CallCredentials.FromInterceptor((context, metadata) => authInterceptor(context, metadata));

                callOptionsContext.CallOptions = ResolveCallOptionsCredentials(callOptionsContext.CallOptions, credentials);
            });
        });

        return builder;
    }

    /// <summary>
    /// Adds a delegate that will be used to create <see cref="CallCredentials"/> for a gRPC call.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="authInterceptor">A delegate that is used to create <see cref="CallCredentials"/> for a gRPC call.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddCallCredentials(this IHttpClientBuilder builder, Func<AuthInterceptorContext, Metadata, IServiceProvider, Task> authInterceptor)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(authInterceptor);

        ValidateGrpcClient(builder, nameof(AddCallCredentials));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.HasCallCredentials = true;
            options.CallOptionsActions.Add((callOptionsContext) =>
            {
                var credentials = CallCredentials.FromInterceptor((context, metadata) => authInterceptor(context, metadata, callOptionsContext.ServiceProvider));

                callOptionsContext.CallOptions = ResolveCallOptionsCredentials(callOptionsContext.CallOptions, credentials);
            });
        });

        return builder;
    }

    /// <summary>
    /// Adds <see cref="CallCredentials"/> for a gRPC call.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="credentials">The <see cref="CallCredentials"/> for a gRPC call.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddCallCredentials(this IHttpClientBuilder builder, CallCredentials credentials)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(credentials);

        ValidateGrpcClient(builder, nameof(AddCallCredentials));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.HasCallCredentials = true;
            options.CallOptionsActions.Add((callOptionsContext) =>
            {
                callOptionsContext.CallOptions = ResolveCallOptionsCredentials(callOptionsContext.CallOptions, credentials);
            });
        });

        return builder;
    }

    private static CallOptions ResolveCallOptionsCredentials(CallOptions callOptions, CallCredentials credentials)
    {
        if (callOptions.Credentials != null)
        {
            credentials = CallCredentials.Compose(callOptions.Credentials, credentials);
        }

        return callOptions.WithCredentials(credentials);
    }

    /// <summary>
    /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
    /// The interceptor scope is <see cref="InterceptorScope.Channel"/>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, Func<Interceptor> configureInvoker)
    {
        return builder.AddInterceptor(InterceptorScope.Channel, configureInvoker);
    }

    /// <summary>
    /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="scope">The scope of the interceptor.</param>
    /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, InterceptorScope scope, Func<Interceptor> configureInvoker)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureInvoker);

        ValidateGrpcClient(builder, nameof(AddInterceptor));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.InterceptorRegistrations.Add(new InterceptorRegistration(scope, s => configureInvoker()));
        });

        return builder;
    }

    /// <summary>
    /// Adds an additional interceptor from the dependency injection container for a gRPC client.
    /// The interceptor scope is <see cref="InterceptorScope.Channel"/>.
    /// </summary>
    /// <typeparam name="TInterceptor">The type of the <see cref="Interceptor"/>.</typeparam>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddInterceptor<TInterceptor>(this IHttpClientBuilder builder)
        where TInterceptor : Interceptor
    {
        return builder.AddInterceptor<TInterceptor>(InterceptorScope.Channel);
    }

    /// <summary>
    /// Adds an additional interceptor from the dependency injection container for a gRPC client.
    /// </summary>
    /// <typeparam name="TInterceptor">The type of the <see cref="Interceptor"/>.</typeparam>
    /// <param name="scope">The scope of the interceptor.</param>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder AddInterceptor<TInterceptor>(this IHttpClientBuilder builder, InterceptorScope scope)
        where TInterceptor : Interceptor
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);

        ValidateGrpcClient(builder, nameof(AddInterceptor));

        builder.AddInterceptor(scope, serviceProvider =>
        {
            return serviceProvider.GetRequiredService<TInterceptor>();
        });

        return builder;
    }

    /// <summary>
    /// Adds a delegate that will be used to create the gRPC client. Clients returned by the delegate must
    /// be compatible with the client type from <c>AddGrpcClient</c>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureCreator">A delegate that is used to create the gRPC client.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder ConfigureGrpcClientCreator(this IHttpClientBuilder builder, Func<IServiceProvider, CallInvoker, object> configureCreator)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureCreator);

        ValidateGrpcClient(builder, nameof(ConfigureGrpcClientCreator));

        builder.Services.AddTransient<IConfigureOptions<GrpcClientFactoryOptions>>(services =>
        {
            return new ConfigureNamedOptions<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.Creator = (callInvoker) => configureCreator(services, callInvoker);
            });
        });

        return builder;
    }

    /// <summary>
    /// Adds a delegate that will be used to create the gRPC client. Clients returned by the delegate must
    /// be compatible with the client type from <c>AddGrpcClient</c>.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureCreator">A delegate that is used to create the gRPC client.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder ConfigureGrpcClientCreator(this IHttpClientBuilder builder, Func<CallInvoker, object> configureCreator)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureCreator);

        ValidateGrpcClient(builder, nameof(ConfigureGrpcClientCreator));

        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
        {
            options.Creator = (callInvoker) => configureCreator(callInvoker);
        });

        return builder;
    }

    private static void ValidateGrpcClient(IHttpClientBuilder builder, string caller)
    {
        // Validate the builder is for a gRPC client
        foreach (var service in builder.Services)
        {
            if (service.ServiceType == typeof(IConfigureOptions<GrpcClientFactoryOptions>))
            {
                // Builder is from AddGrpcClient if options have been configured with the same name
                var namedOptions = service.ImplementationInstance as ConfigureNamedOptions<GrpcClientFactoryOptions>;
                if (namedOptions != null && string.Equals(builder.Name, namedOptions.Name, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"{caller} must be used with a gRPC client.");
    }
}
