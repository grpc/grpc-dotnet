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
using System.Reflection;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Builder
{
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
        /// <returns>An <see cref="IEndpointConventionBuilder"/> for endpoints associated with the service.</returns>
        public static IEndpointConventionBuilder MapGrpcService<TService>(this IEndpointRouteBuilder builder) where TService : class
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            return builder.MapGrpcService<TService>(configureOptions: null);
        }

        /// <summary>
        /// Maps incoming requests to the specified <typeparamref name="TService"/> type.
        /// </summary>
        /// <typeparam name="TService">The service type to map requests to.</typeparam>
        /// <param name="builder">The <see cref="IEndpointRouteBuilder"/> to add the route to.</param>
        /// <param name="configureOptions">A callback to configure binding options.</param>
        /// <returns>An <see cref="IEndpointConventionBuilder"/> for endpoints associated with the service.</returns>
        public static IEndpointConventionBuilder MapGrpcService<TService>(this IEndpointRouteBuilder builder, Action<GrpcBindingOptions<TService>> configureOptions) where TService : class
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ValidateServicesRegistered(builder.ServiceProvider);

            var options = new GrpcBindingOptions<TService>();
            options.BindAction = ReflectionBind;
            options.ModelFactory = new ReflectionMethodModelFactory<TService>();

            configureOptions?.Invoke(options);

            var callHandlerFactory = builder.ServiceProvider.GetRequiredService<ServerCallHandlerFactory<TService>>();
            var serviceMethodsRegistry = builder.ServiceProvider.GetRequiredService<ServiceMethodsRegistry>();

            var serviceBinder = new GrpcServiceBinder<TService>(builder, options.ModelFactory, callHandlerFactory, serviceMethodsRegistry);

            try
            {
                options.BindAction(serviceBinder, null);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error binding gRPC service '{typeof(TService).Name}'.", ex);
            }

            serviceBinder.CreateUnimplementedEndpoints();

            return new CompositeEndpointConventionBuilder(serviceBinder.EndpointConventionBuilders);
        }

        private static void ReflectionBind<TService>(ServiceBinderBase binder, TService service)
        {
            var serviceType = typeof(TService);

            // TService is an implementation of the gRPC service. It ultimately derives from Foo.TServiceBase base class.
            // We need to access the static BindService method on Foo which implicitly derives from Object.
            var baseType = serviceType.BaseType;

            // Handle services that have multiple levels of inheritence
            while (baseType?.BaseType?.BaseType != null)
            {
                baseType = baseType.BaseType;
            }

            // We need to call Foo.BindService from the declaring type.
            var declaringType = baseType?.DeclaringType;

            // The method we want to call is public static void BindService(ServiceBinderBase, BaseType)
            var bindService = declaringType?.GetMethod("BindService", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ServiceBinderBase), baseType }, Array.Empty<ParameterModifier>());

            if (bindService == null)
            {
                throw new InvalidOperationException($"Cannot locate BindService(ServiceBinderBase, ServiceBase) method for the current service type: {serviceType.FullName}.");
            }

            // Invoke BindService(ServiceBinderBase, BaseType)
            bindService.Invoke(null, new object[] { binder, service });
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
}
