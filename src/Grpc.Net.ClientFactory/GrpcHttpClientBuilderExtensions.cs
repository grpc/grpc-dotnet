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
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Net.Client;
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
        /// Adds a delegate that will be used to configure the channel for a gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <param name="configureChannel">A delegate that is used to configure a <see cref="GrpcChannelOptions"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder ConfigureChannel(this IHttpClientBuilder builder, Action<IServiceProvider, GrpcChannelOptions> configureChannel)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureChannel == null)
            {
                throw new ArgumentNullException(nameof(configureChannel));
            }

            ValidateGrpcClient(builder);

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
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureChannel == null)
            {
                throw new ArgumentNullException(nameof(configureChannel));
            }

            ValidateGrpcClient(builder);

            builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.ChannelOptionsActions.Add(configureChannel);
            });

            return builder;
        }

        /// <summary>
        /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, Func<IServiceProvider, Interceptor> configureInvoker)
        {
            return builder.AddInterceptor(InterceptorLifetime.Client, configureInvoker);
        }

        /// <summary>
        /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <param name="lifetime">The lifetime of the interceptor.</param>
        /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, InterceptorLifetime lifetime, Func<IServiceProvider, Interceptor> configureInvoker)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureInvoker == null)
            {
                throw new ArgumentNullException(nameof(configureInvoker));
            }

            ValidateGrpcClient(builder);

            builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.InterceptorRegistrations.Add(new InterceptorRegistration(lifetime, configureInvoker));
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
            return builder.AddInterceptor(InterceptorLifetime.Client, configureInvoker);
        }

        /// <summary>
        /// Adds a delegate that will be used to create an additional inteceptor for a gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <param name="lifetime">The lifetime of the interceptor.</param>
        /// <param name="configureInvoker">A delegate that is used to create an <see cref="Interceptor"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor(this IHttpClientBuilder builder, InterceptorLifetime lifetime, Func<Interceptor> configureInvoker)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureInvoker == null)
            {
                throw new ArgumentNullException(nameof(configureInvoker));
            }

            ValidateGrpcClient(builder);

            builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.InterceptorRegistrations.Add(new InterceptorRegistration(lifetime, s => configureInvoker()));
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
            return builder.AddInterceptor<TInterceptor>(InterceptorLifetime.Client);
        }

        /// <summary>
        /// Adds an additional interceptor from the dependency injection container for a gRPC client.
        /// </summary>
        /// <typeparam name="TInterceptor">The type of the <see cref="Interceptor"/>.</typeparam>
        /// <param name="lifetime">The lifetime of the interceptor.</param>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder AddInterceptor<TInterceptor>(this IHttpClientBuilder builder, InterceptorLifetime lifetime)
            where TInterceptor : Interceptor
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ValidateGrpcClient(builder);

            builder.AddInterceptor(lifetime, serviceProvider =>
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
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureCreator == null)
            {
                throw new ArgumentNullException(nameof(configureCreator));
            }

            ValidateGrpcClient(builder);

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
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (configureCreator == null)
            {
                throw new ArgumentNullException(nameof(configureCreator));
            }

            ValidateGrpcClient(builder);

            builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, options =>
            {
                options.Creator = (callInvoker) => configureCreator(callInvoker);
            });

            return builder;
        }

        private static void ValidateGrpcClient(IHttpClientBuilder builder)
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

            throw new InvalidOperationException($"{nameof(AddInterceptor)} must be used with a gRPC client.");
        }
    }
}
