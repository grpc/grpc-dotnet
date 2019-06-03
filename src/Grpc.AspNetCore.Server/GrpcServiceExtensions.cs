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
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model;
using Grpc.AspNetCore.Server.Model.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
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
            if (grpcBuilder == null)
            {
                throw new ArgumentNullException(nameof(grpcBuilder));
            }

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
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.AddRouting();
            services.AddOptions();
            services.TryAddSingleton<GrpcMarkerService>();
            services.TryAddSingleton(typeof(ServerCallHandlerFactory<>));
            services.TryAddScoped(typeof(IGrpcServiceActivator<>), typeof(DefaultGrpcServiceActivator<>));
            services.TryAddScoped(typeof(IGrpcInterceptorActivator<>), typeof(DefaultGrpcInterceptorActivator<>));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<GrpcServiceOptions>, GrpcServiceOptionsSetup>());
            
            // Model
            services.TryAddSingleton<ServiceMethodsRegistry>();
            services.TryAddSingleton(typeof(ServiceRouteBuilder<>));
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(BinderServiceMethodProvider<>)));

            return new GrpcServerBuilder(services);
        }

        /// <summary>
        /// Adds gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <param name="configureOptions">An <see cref="Action{GrpcServiceOptions}"/> to configure the provided <see cref="GrpcServiceOptions"/>.</param>
        /// <returns>An <see cref="IGrpcServerBuilder"/> that can be used to further configure the gRPC services.</returns>
        public static IGrpcServerBuilder AddGrpc(this IServiceCollection services, Action<GrpcServiceOptions> configureOptions)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            return services.Configure(configureOptions).AddGrpc();
        }
    }
}
