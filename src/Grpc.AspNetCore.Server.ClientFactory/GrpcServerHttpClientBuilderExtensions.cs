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
using Grpc.AspNetCore.Server.ClientFactory;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Extension methods for configuring an <see cref="IHttpClientBuilder"/>.
    /// </summary>
    public static class GrpcServerHttpClientBuilderExtensions
    {
        /// <summary>
        /// Configures the server to propagate values from a call's <see cref="ServerCallContext"/>
        /// onto the gRPC client.
        /// </summary>
        /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
        /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
        public static IHttpClientBuilder EnableCallContextPropagation(this IHttpClientBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ValidateGrpcClient(builder);

            builder.Services.TryAddScoped<ContextPropagationInterceptor>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddTransient<IConfigureOptions<GrpcClientFactoryOptions>>(services =>
            {
                return new ConfigureNamedOptions<GrpcClientFactoryOptions>(builder.Name, options =>
                {
                    options.Interceptors.Add(services.GetRequiredService<ContextPropagationInterceptor>());
                });
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

            throw new InvalidOperationException($"{nameof(EnableCallContextPropagation)} must be used with a gRPC client.");
        }
    }
}
