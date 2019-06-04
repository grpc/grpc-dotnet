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
using Grpc.Core.Interceptors;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for configuring an <see cref="IHttpClientBuilder"/>.
    /// </summary>
    public static class GrpcHttpClientBuilderExtensions
    {
        /// <summary>
        /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, Func<IServiceProvider, Interceptor> configureInvoker)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureInvoker == null)
            {
                throw new ArgumentNullException(nameof(configureInvoker));
            }

            builder.Services.AddTransient<IConfigureOptions<GrpcClientFactoryOptions>>(services =>
            {
                return new ConfigureNamedOptions<GrpcClientFactoryOptions>(builder.Name, (options) =>
                {
                    options.Interceptors.Add(configureInvoker(services));
                });
            });

            return builder;
        }

        /// <summary>
        /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, Func<Interceptor> configureInvoker)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureInvoker == null)
            {
                throw new ArgumentNullException(nameof(configureInvoker));
            }

            builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.Interceptors.Add(configureInvoker());
            });

            return builder;
        }

        /// <summary>
        /// Adds an additional interceptor from the dependency injection container for a gRPC client.
        /// </summary>
        /// <typeparam name="TInterceptor">The type of the <see cref="Interceptor"/>.</typeparam>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor<TInterceptor>(this IHttpClientBuilder builder)
            where TInterceptor : Interceptor
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddInterceptor(serviceProvider =>
            {
                return serviceProvider.GetRequiredService<TInterceptor>();
            });

            return builder;
        }
    }
}
