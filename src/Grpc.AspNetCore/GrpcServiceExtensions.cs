using System;
using GRPCServer.Dotnet;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for the Grpc services.
    /// </summary>
    public static class GrpcServicesExtensions
    {
        /// <summary>
        /// Add a GRPC service implementation.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns></returns>
        // TODO: Options?
        public static IServiceCollection AddGrpc(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddScoped(typeof(IGrpcServiceActivator<>), typeof(DefaultGrpcServiceActivator<>));

            return services;
        }
    }
}
