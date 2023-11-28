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

using System.Diagnostics.CodeAnalysis;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for the gRPC services.
/// </summary>
public static class GrpcServicesExtensions
{
    /// <summary>
    /// Adds service specific options to an <see cref="IGrpcServerBuilder"/>.
    /// </summary>
    /// <typeparam name="TService">The service type to configure.</typeparam>
    /// <param name="grpcBuilder">The <see cref="IGrpcServerBuilder"/>.</param>
    /// <param name="configure">A callback to configure the service options.</param>
    /// <returns>The same instance of the <see cref="IGrpcServerBuilder"/> for chaining.</returns>
    public static IGrpcServerBuilder AddServiceOptions<TService>(this IGrpcServerBuilder grpcBuilder, Action<GrpcServiceOptions<TService>> configure) where TService : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(grpcBuilder);

        grpcBuilder.Services.AddSingleton<IConfigureOptions<GrpcServiceOptions<TService>>, GrpcServiceOptionsSetup<TService>>();
        grpcBuilder.Services.Configure(configure);
        return grpcBuilder;
    }

    /// <summary>
    /// Adds gRPC services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
    /// <returns>An <see cref="IGrpcServerBuilder"/> that can be used to further configure the gRPC services.</returns>
    public static IGrpcServerBuilder AddGrpc(this IServiceCollection services)
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);

#if NET8_0_OR_GREATER
        // Prefer AddRoutingCore when available.
        // AddRoutingCore doesn't register a regex constraint and produces smaller result from trimming.
        services.AddRoutingCore();
        services.Configure<RouteOptions>(ConfigureRouting);
#else
        services.AddRouting(ConfigureRouting);
#endif
        services.AddOptions();
        services.TryAddSingleton<GrpcMarkerService>();
        services.TryAddSingleton(typeof(ServerCallHandlerFactory<>));
        services.TryAddSingleton(typeof(IGrpcServiceActivator<>), typeof(DefaultGrpcServiceActivator<>));
        services.TryAddSingleton(typeof(IGrpcInterceptorActivator<>), typeof(DefaultGrpcInterceptorActivator<>));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<GrpcServiceOptions>, GrpcServiceOptionsSetup>());

        // Model
        services.TryAddSingleton<ServiceMethodsRegistry>();
        services.TryAddSingleton(typeof(ServiceRouteBuilder<>));
        services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(BinderServiceMethodProvider<>)));

        return new GrpcServerBuilder(services);

        static void ConfigureRouting(RouteOptions options)
        {
            // Unimplemented constraint is added to the route as an inline constraint to avoid RoutePatternFactory.Parse overload that includes parameter policies. That overload infers strings as regex constraints, which brings in
            // the regex engine when publishing trimmed or AOT apps. This change reduces Native AOT gRPC server app size by about 1 MB.
            AddParameterPolicy<GrpcUnimplementedConstraint>(options, GrpcServerConstants.GrpcUnimplementedConstraintPrefix);
        }

        // This ensures the policy's constructors are preserved in .NET 6 with trimming. Remove when .NET 6 is no longer supported.
        static void AddParameterPolicy<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(RouteOptions options, string name)
            where T : IParameterPolicy
        {
#if NET7_0_OR_GREATER
            options.SetParameterPolicy<T>(name);
#else
            options.ConstraintMap[name] = typeof(T);
#endif
        }
    }

    /// <summary>
    /// Adds gRPC services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
    /// <param name="configureOptions">An <see cref="Action{GrpcServiceOptions}"/> to configure the provided <see cref="GrpcServiceOptions"/>.</param>
    /// <returns>An <see cref="IGrpcServerBuilder"/> that can be used to further configure the gRPC services.</returns>
    public static IGrpcServerBuilder AddGrpc(this IServiceCollection services, Action<GrpcServiceOptions> configureOptions)
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);

        return services.Configure(configureOptions).AddGrpc();
    }
}
