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
using System.Net.Http;
using Grpc.AspNetCore.Server.Features;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Microsoft.AspNetCore.Http;
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

            builder.Services.AddHttpContextAccessor();
            builder.Services.AddTransient<IConfigureOptions<GrpcClientFactoryOptions>>(services =>
            {
                return new ConfigureNamedOptions<GrpcClientFactoryOptions>(builder.Name, (options) =>
                {
                    options.CallInvokerActions.Add(client =>
                    {
                        var httpContextAccessor = services.GetRequiredService<IHttpContextAccessor>();

                        var httpContext = httpContextAccessor.HttpContext;
                        if (httpContext == null)
                        {
                            throw new InvalidOperationException("Unable to propagate server context values to the client. Can't find the current HttpContext.");
                        }

                        var serverCallContext = httpContext.Features.Get<IServerCallContextFeature>()?.ServerCallContext;
                        if (serverCallContext == null)
                        {
                            throw new InvalidOperationException("Unable to propagate server context values to the client. Can't find the current gRPC ServerCallContext.");
                        }

                        client.CancellationToken = serverCallContext.CancellationToken;
                        client.Deadline = serverCallContext.Deadline;
                    });
                });
            });

            return builder;
        }
    }
}
