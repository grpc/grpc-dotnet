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
using System.Collections.Generic;
using System.Linq;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal class ServiceRouteBuilder<TService> where TService : class
    {
        private readonly IEnumerable<IServiceMethodProvider<TService>> _serviceMethodProviders;
        private readonly ServerCallHandlerFactory<TService> _serverCallHandlerFactory;
        private readonly ServiceMethodsRegistry _serviceMethodsRegistry;
        private readonly ILogger _logger;

        public ServiceRouteBuilder(
            IEnumerable<IServiceMethodProvider<TService>> serviceMethodProviders,
            ServerCallHandlerFactory<TService> serverCallHandlerFactory,
            ServiceMethodsRegistry serviceMethodsRegistry,
            ILoggerFactory loggerFactory)
        {
            _serviceMethodProviders = serviceMethodProviders.ToList();
            _serverCallHandlerFactory = serverCallHandlerFactory;
            _serviceMethodsRegistry = serviceMethodsRegistry;
            _logger = loggerFactory.CreateLogger<ServiceRouteBuilder<TService>>();
        }

        internal List<IEndpointConventionBuilder> Build(IEndpointRouteBuilder endpointRouteBuilder)
        {
            Log.DiscoveringServiceMethods(_logger, typeof(TService));

            var serviceMethodProviderContext = new ServiceMethodProviderContext<TService>(_serverCallHandlerFactory);
            foreach (var serviceMethodProvider in _serviceMethodProviders)
            {
                serviceMethodProvider.OnServiceMethodDiscovery(serviceMethodProviderContext);
            }

            var endpointConventionBuilders = new List<IEndpointConventionBuilder>();
            if (serviceMethodProviderContext.Methods.Count > 0)
            {
                foreach (var method in serviceMethodProviderContext.Methods)
                {
                    var endpointBuilder = endpointRouteBuilder.Map(method.Pattern, method.RequestDelegate);

                    endpointBuilder.Add(ep =>
                    {
                        ep.DisplayName = $"gRPC - {method.Pattern.RawText}";

                        ep.Metadata.Add(new GrpcMethodMetadata(typeof(TService), method.Method));
                        foreach (var item in method.Metadata)
                        {
                            ep.Metadata.Add(item);
                        }
                    });

                    endpointConventionBuilders.Add(endpointBuilder);

                    Log.AddedServiceMethod(_logger, method.Method.Name, method.Method.ServiceName, method.Method.Type, method.Pattern.RawText ?? string.Empty);
                }
            }
            else
            {
                Log.NoServiceMethodsDiscovered(_logger, typeof(TService));
            }

            CreateUnimplementedEndpoints(
                endpointRouteBuilder,
                _serviceMethodsRegistry,
                _serverCallHandlerFactory,
                serviceMethodProviderContext.Methods,
                endpointConventionBuilders);

            _serviceMethodsRegistry.Methods.AddRange(serviceMethodProviderContext.Methods);

            return endpointConventionBuilders;
        }

        internal static void CreateUnimplementedEndpoints(
            IEndpointRouteBuilder endpointRouteBuilder,
            ServiceMethodsRegistry serviceMethodsRegistry,
            ServerCallHandlerFactory<TService> serverCallHandlerFactory,
            List<MethodModel> serviceMethods,
            List<IEndpointConventionBuilder> endpointConventionBuilders)
        {
            // Return UNIMPLEMENTED status for missing service:
            // - /{service}/{method} + content-type header = grpc/application
            if (!serverCallHandlerFactory.IgnoreUnknownServices && serviceMethodsRegistry.Methods.Count == 0)
            {
                // Only one unimplemented service endpoint is needed for the application
                endpointConventionBuilders.Add(CreateUnimplementedEndpoint(endpointRouteBuilder, "{unimplementedService}/{unimplementedMethod}", "Unimplemented service", serverCallHandlerFactory.CreateUnimplementedService()));
            }

            // Return UNIMPLEMENTED status for missing method:
            // - /Package.Service/{method} + content-type header = grpc/application
            if (!serverCallHandlerFactory.IgnoreUnknownMethods)
            {
                var serviceNames = serviceMethods.Select(m => m.Method.ServiceName).Distinct();

                // Typically there should be one service name for a type
                // In case the bind method sets up multiple services in one call we'll loop over them
                foreach (var serviceName in serviceNames)
                {
                    if (serviceMethodsRegistry.Methods.Any(m => string.Equals(m.Method.ServiceName, serviceName, StringComparison.Ordinal)))
                    {
                        // Only one unimplemented method endpoint is need for the service
                        continue;
                    }

                    endpointConventionBuilders.Add(CreateUnimplementedEndpoint(endpointRouteBuilder, serviceName + "/{unimplementedMethod}", $"Unimplemented method for {serviceName}", serverCallHandlerFactory.CreateUnimplementedMethod()));
                }
            }
        }

        private static IEndpointConventionBuilder CreateUnimplementedEndpoint(IEndpointRouteBuilder endpointRouteBuilder, string pattern, string displayName, RequestDelegate requestDelegate)
        {
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
            // https://github.com/dotnet/aspnetcore/issues/24042
            var routePattern = RoutePatternFactory.Parse(pattern, defaults: null, new { contentType = GrpcUnimplementedConstraint.Instance });
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
            var endpointBuilder = endpointRouteBuilder.Map(routePattern, requestDelegate);

            endpointBuilder.Add(ep =>
            {
                ep.DisplayName = $"gRPC - {displayName}";
                // Don't add POST metadata here. It will return 405 status for other HTTP methods which isn't
                // what we want. That check is made in a constraint instead.
            });

            return endpointBuilder;
        }

        private class GrpcUnimplementedConstraint : IRouteConstraint
        {
            public static readonly GrpcUnimplementedConstraint Instance = new GrpcUnimplementedConstraint();

            public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
            {
                if (httpContext == null)
                {
                    return false;
                }

                // Constraint needs to be valid when a CORS preflight request is received so that CORS middleware will run
                if (GrpcProtocolHelpers.IsCorsPreflightRequest(httpContext))
                {
                    return true;
                }

                if (!HttpMethods.IsPost(httpContext.Request.Method))
                {
                    return false;
                }

                return CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcContentType, httpContext.Request.ContentType) ||
                    CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcWebContentType, httpContext.Request.ContentType) ||
                    CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcWebTextContentType, httpContext.Request.ContentType);
            }

            private GrpcUnimplementedConstraint()
            {
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, string, MethodType, string, Exception?> _addedServiceMethod =
                LoggerMessage.Define<string, string, MethodType, string>(LogLevel.Trace, new EventId(1, "AddedServiceMethod"), "Added gRPC method '{MethodName}' to service '{ServiceName}'. Method type: '{MethodType}', route pattern: '{RoutePattern}'.");

            private static readonly Action<ILogger, Type, Exception?> _discoveringServiceMethods =
                LoggerMessage.Define<Type>(LogLevel.Trace, new EventId(2, "DiscoveringServiceMethods"), "Discovering gRPC methods for {ServiceType}.");

            private static readonly Action<ILogger, Type, Exception?> _noServiceMethodsDiscovered =
                LoggerMessage.Define<Type>(LogLevel.Debug, new EventId(3, "NoServiceMethodsDiscovered"), "No gRPC methods discovered for {ServiceType}.");

            public static void AddedServiceMethod(ILogger logger, string methodName, string serviceName, MethodType methodType, string routePattern)
            {
                _addedServiceMethod(logger, methodName, serviceName, methodType, routePattern, null);
            }

            public static void DiscoveringServiceMethods(ILogger logger, Type serviceType)
            {
                _discoveringServiceMethods(logger, serviceType, null);
            }

            public static void NoServiceMethodsDiscovered(ILogger logger, Type serviceType)
            {
                _noServiceMethodsDiscovered(logger, serviceType, null);
            }
        }
    }
}
