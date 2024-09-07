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

using System.Diagnostics.CodeAnalysis;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add gRPC service endpoints.
/// </summary>
public static class GrpcEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps incoming requests to the specified <typeparamref name="TService"/> type.
    /// </summary>
    /// <typeparam name="TService">The service type to map requests to.</typeparam>
    /// <param name="builder">The <see cref="IEndpointRouteBuilder"/> to add the route to.</param>
    /// <returns>A <see cref="GrpcServiceEndpointConventionBuilder"/> for endpoints associated with the service.</returns>
    public static GrpcServiceEndpointConventionBuilder MapGrpcService<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService>(this IEndpointRouteBuilder builder) where TService : class
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);

        ValidateServicesRegistered(builder.ServiceProvider);

        var serviceRouteBuilder = builder.ServiceProvider.GetRequiredService<ServiceRouteBuilder<TService>>();
        var endpointConventionBuilders = serviceRouteBuilder.Build(builder);

        return new GrpcServiceEndpointConventionBuilder(endpointConventionBuilders);
    }

    private static void ValidateServicesRegistered(IServiceProvider serviceProvider)
    {
        var marker = serviceProvider.GetService(typeof(GrpcMarkerService));
        if (marker == null)
        {
            throw new InvalidOperationException("Unable to find the required services. Please add all the required services by calling " +
                "'IServiceCollection.AddGrpc' inside the call to 'ConfigureServices(...)' in the application startup code.");
        }
    }
}
