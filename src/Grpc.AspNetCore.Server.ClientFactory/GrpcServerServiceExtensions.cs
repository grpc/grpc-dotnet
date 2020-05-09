using System;
using Grpc.AspNetCore.ClientFactory;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Grpc.AspNetCore.Server.ClientFactory
{
    /// <summary>
    /// Extension methods for <see cref="IServiceCollection"/>.
    /// </summary>
    public static class GrpcServerServiceExtensions
    {
        /// <summary>
        /// Configures the server to propagate values from a call's <see cref="ServerCallContext"/>
        /// onto all gRPC clients.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/>.</param>
        /// <returns>The service collection.</returns>
        public static IServiceCollection AddGrpcCallContextPropagation(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddSingleton<ContextPropagationInterceptor>();
            services.AddHttpContextAccessor();
            services.ConfigureAllGrpcClients((services, options) =>
            {
                options.Interceptors.Add(services.GetRequiredService<ContextPropagationInterceptor>());
            });

            return services;
        }
    }
}