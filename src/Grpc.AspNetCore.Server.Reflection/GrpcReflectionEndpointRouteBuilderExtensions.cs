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

using Grpc.Reflection;
using Grpc.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Provides extension methods for <see cref="IEndpointRouteBuilder"/> to add gRPC service endpoints.
/// </summary>
public static class GrpcReflectionEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps incoming requests to the gRPC reflection service.
    /// This service can be queried to discover the gRPC services on the server.
    /// </summary>
    /// <param name="builder">The <see cref="IEndpointRouteBuilder"/> to add the route to.</param>
    /// <returns>An <see cref="IEndpointConventionBuilder"/> for endpoints associated with the service.</returns>
    public static IEndpointConventionBuilder MapGrpcReflectionService(this IEndpointRouteBuilder builder)
    {
        ArgumentNullThrowHelper.ThrowIfNull(builder);

        ValidateServicesRegistered(builder.ServiceProvider);

        return builder.MapGrpcService<ReflectionServiceImpl>();
    }

    private static void ValidateServicesRegistered(IServiceProvider serviceProvider)
    {
        var marker = serviceProvider.GetService(typeof(GrpcReflectionMarkerService));
        if (marker == null)
        {
            throw new InvalidOperationException("Unable to find the required services. Please add all the required services by calling " +
                "'IServiceCollection.AddGrpcReflection()' inside the call to 'ConfigureServices(...)' in the application startup code.");
        }
    }
}
