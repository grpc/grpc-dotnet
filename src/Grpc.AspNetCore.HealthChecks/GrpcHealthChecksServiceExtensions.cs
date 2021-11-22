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

using Grpc.AspNetCore.HealthChecks;
using Grpc.HealthCheck;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for the gRPC health checks services.
    /// </summary>
    public static class GrpcHealthChecksServiceExtensions
    {
        /// <summary>
        /// Adds gRPC health check services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
        /// <returns>An instance of <see cref="IHealthChecksBuilder"/> from which health checks can be registered.</returns>
        public static IHealthChecksBuilder AddGrpcHealthChecks(this IServiceCollection services)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            // HealthServiceImpl is designed to be a singleton
            services.TryAddSingleton<HealthServiceImpl>();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthCheckPublisher, GrpcHealthChecksPublisher>());

            return services.AddHealthChecks();
        }
    }
}
