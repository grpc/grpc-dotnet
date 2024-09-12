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
using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Reflection;
using Grpc.Shared;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for the gRPC reflection services.
/// </summary>
public static partial class GrpcReflectionServiceExtensions
{
    /// <summary>
    /// Adds gRPC reflection services to the specified <see cref="IServiceCollection" />.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> for adding services.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddGrpcReflection(this IServiceCollection services)
    {
        ArgumentNullThrowHelper.ThrowIfNull(services);

        services.TryAddSingleton<GrpcReflectionMarkerService>();

        // ReflectionServiceImpl is designed to be a singleton
        // Explicitly register creating it in DI using descriptors calculated from gRPC endpoints in the app
        services.TryAddSingleton<ReflectionServiceImpl>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(GrpcReflectionServiceExtensions));
            var endpointDataSource = serviceProvider.GetRequiredService<EndpointDataSource>();

            var grpcEndpointMetadata = endpointDataSource.Endpoints
                .Select(ep => ep.Metadata.GetMetadata<GrpcMethodMetadata>())
                .OfType<GrpcMethodMetadata>()
                .ToList();

            var serviceTypes = grpcEndpointMetadata.Select(m => m.ServiceType).Distinct().ToList();

            var serviceDescriptors = new List<Google.Protobuf.Reflection.ServiceDescriptor>();

            foreach (var serviceType in serviceTypes)
            {
                var descriptorPropertyInfo = GetDescriptorProperty(serviceType);
                if (descriptorPropertyInfo != null)
                {
                    if (descriptorPropertyInfo.GetValue(null) is Google.Protobuf.Reflection.ServiceDescriptor serviceDescriptor)
                    {
                        serviceDescriptors.Add(serviceDescriptor);
                        continue;
                    }
                }

                Log.ServiceDescriptorNotResolved(logger, serviceType);
            }

            return new ReflectionServiceImpl(serviceDescriptors);
        });

        return services;
    }

    private static PropertyInfo? GetDescriptorProperty(Type serviceType)
    {
        // Prefer finding the descriptor property using attribute on the generated service
        var descriptorPropertyInfo = GetDescriptorPropertyUsingAttribute(serviceType);

        if (descriptorPropertyInfo == null)
        {
            // Fallback to searching for descriptor property using known type hierarchy that Grpc.Tools generates
            descriptorPropertyInfo = GetDescriptorPropertyFallback(serviceType);
        }

        return descriptorPropertyInfo;
    }

    private static PropertyInfo? GetDescriptorPropertyUsingAttribute(Type serviceType)
    {
        Type? currentServiceType = serviceType;
        BindServiceMethodAttribute? bindServiceMethod;
        do
        {
            // Search through base types for bind service attribute.
            bindServiceMethod = currentServiceType.GetCustomAttribute<BindServiceMethodAttribute>();
            if (bindServiceMethod != null)
            {
                // Descriptor property will be public and static and return ServiceDescriptor.
                return bindServiceMethod.BindType.GetProperty(
                    "Descriptor",
                    BindingFlags.Public | BindingFlags.Static,
                    binder: null,
                    typeof(Google.Protobuf.Reflection.ServiceDescriptor),
                    Type.EmptyTypes,
                    Array.Empty<ParameterModifier>());
            }
        } while ((currentServiceType = currentServiceType.BaseType) != null);

        return null;
    }

    [SuppressMessage("Trimming", "IL2075", Justification = "Modern apps published with trimming will be using newer Grpc.Tools and won't use this fallback.")]
    private static PropertyInfo? GetDescriptorPropertyFallback(Type serviceType)
    {
        // Search for the generated service base class
        var baseType = GetServiceBaseType(serviceType);
        var definitionType = baseType?.DeclaringType;

        return definitionType?.GetProperty(
            "Descriptor",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            typeof(Google.Protobuf.Reflection.ServiceDescriptor),
            Type.EmptyTypes,
            Array.Empty<ParameterModifier>());
    }

    private static Type? GetServiceBaseType(Type serviceImplementation)
    {
        // TService is an implementation of the gRPC service. It ultimately derives from Foo.TServiceBase base class.
        // We need to access the static BindService method on Foo which implicitly derives from Object.
        var baseType = serviceImplementation.BaseType;

        // Handle services that have multiple levels of inheritence
        while (baseType?.BaseType?.BaseType != null)
        {
            baseType = baseType.BaseType;
        }

        return baseType;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "ServiceDescriptorNotResolved", Message = "Could not resolve service descriptor for '{ServiceType}'. The service metadata will not be exposed by the reflection service.")]
        private static partial void ServiceDescriptorNotResolved(ILogger logger, string serviceType);
        public static void ServiceDescriptorNotResolved(ILogger logger, Type serviceType)
        {
            ServiceDescriptorNotResolved(logger, serviceType.FullName ?? string.Empty);
        }
    }
}
