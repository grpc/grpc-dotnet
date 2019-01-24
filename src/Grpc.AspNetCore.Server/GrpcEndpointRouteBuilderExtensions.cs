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
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GrpcEndpointRouteBuilderExtensions
    {
        public static IEndpointConventionBuilder MapGrpcService<TService>(this IEndpointRouteBuilder builder) where TService : class
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            ValidateServicesRegistered(builder.ServiceProvider);

            var service = typeof(TService);

            try
            {
                // Implementation type FooImpl derives from Foo.FooBase (with implicit base type of Object).
                var baseType = service.BaseType;
                while (baseType?.BaseType?.BaseType != null)
                {
                    baseType = baseType.BaseType;
                }

                // We need to call Foo.BindService from the declaring type.
                var declaringType = baseType?.DeclaringType;

                // The method we want to call is public static void BindService(ServiceBinderBase serviceBinder)
                var bindService = declaringType?.GetMethod("BindService", new[] { typeof(ServiceBinderBase), baseType });

                if (bindService == null)
                {
                    throw new InvalidOperationException("Cannot locate BindService(ServiceBinderBase, ServiceBase) method on generated gRPC service type.");
                }

                var serviceBinder = new GrpcServiceBinder<TService>(builder);

                // Create dummy service instance for the call to BindService.
                // This will fail for services that resolve Scoped services in the constructor.
                // This restriction will be fixed by https://github.com/grpc/grpc-dotnet/issues/21
                var dummy = new DefaultGrpcServiceActivator<TService>(builder.ServiceProvider).Create();

                // Invoke
                bindService.Invoke(null, new object[] { serviceBinder, dummy });

                return new CompositeEndpointConventionBuilder(serviceBinder.EndpointConventionBuilders);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Unable to map gRPC service '{service.Name}'.", ex);
            }
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
