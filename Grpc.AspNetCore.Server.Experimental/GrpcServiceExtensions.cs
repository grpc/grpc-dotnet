using System;
using Grpc.AspNetCore.Server.Model;
using Grpc.AspNetCore.Server.Model.Internal;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for the gRPC services.
    /// </summary>
    public static class GrpcServicesExtensions
    {
        /// <summary>
        /// Adds gRPC services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
        public static IServiceCollection AddGrpcExperimental(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IServiceMethodProvider<>), typeof(ServiceMethodProvider<>)));
            services.TryAddTransient(typeof(IUnaryServerMethodBuilder<>), typeof(UnaryServerMethodBuilder<>));

            return services;
        }
    }
}