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

using Grpc.AspNetCore.ClientFactory;
using Grpc.Core;
using Grpc.Net.ClientFactory;
using Grpc.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for configuring an <see cref="IHttpClientBuilder"/>.
/// </summary>
public static class GrpcServerHttpClientBuilderExtensions
{
    /// <summary>
    /// Configures the server to propagate a call's <see cref="ServerCallContext.CancellationToken"/> and
    /// <see cref="ServerCallContext.Deadline"/> onto the gRPC client.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder EnableCallContextPropagation(this IHttpClientBuilder builder)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);

        EnableCallContextPropagationCore(builder, new GrpcContextPropagationOptions());

        return builder;
    }

    /// <summary>
    /// Configures the server to propagate a call's <see cref="ServerCallContext.CancellationToken"/> and
    /// <see cref="ServerCallContext.Deadline"/> onto the gRPC client.
    /// </summary>
    /// <param name="builder">The <see cref="IHttpClientBuilder"/>.</param>
    /// <param name="configureOptions">An <see cref="Action{GrpcContextPropagationOptions}"/> to configure the provided <see cref="GrpcContextPropagationOptions"/>.</param>
    /// <returns>An <see cref="IHttpClientBuilder"/> that can be used to configure the client.</returns>
    public static IHttpClientBuilder EnableCallContextPropagation(this IHttpClientBuilder builder, Action<GrpcContextPropagationOptions> configureOptions)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);
        ArgumentNullThrowHelper.ThrowIfNull(configureOptions);

        var options = new GrpcContextPropagationOptions();
        configureOptions(options);
        EnableCallContextPropagationCore(builder, options);

        return builder;
    }

    private static void EnableCallContextPropagationCore(IHttpClientBuilder builder, GrpcContextPropagationOptions options)
    {
        ValidateGrpcClient(builder);

        builder.Services.AddHttpContextAccessor();
        builder.Services.Configure<GrpcClientFactoryOptions>(builder.Name, o =>
        {
            o.InterceptorRegistrations.Add(new InterceptorRegistration(
                InterceptorScope.Channel,
                s =>
                {
                    var accessor = s.GetRequiredService<IHttpContextAccessor>();
                    var logger = s.GetRequiredService<ILogger<ContextPropagationInterceptor>>();
                    return new ContextPropagationInterceptor(options, accessor, logger);
                }));
        });
    }

    private static void ValidateGrpcClient(IHttpClientBuilder builder)
    {
        // Validate the builder is for a gRPC client
        foreach (var service in builder.Services)
        {
            if (service.ServiceType == typeof(IConfigureOptions<GrpcClientFactoryOptions>))
            {
                // Builder is from AddGrpcClient if options have been configured with the same name
                if (service.ImplementationInstance is ConfigureNamedOptions<GrpcClientFactoryOptions> namedOptions && string.Equals(builder.Name, namedOptions.Name, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException($"{nameof(EnableCallContextPropagation)} must be used with a gRPC client.");
    }
}
