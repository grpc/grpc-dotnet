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
            var serviceMethodProviderContext = new ServiceMethodProviderContext<TService>(_serverCallHandlerFactory);
            foreach (var serviceMethodProvider in _serviceMethodProviders)
            {
                serviceMethodProvider.OnServiceMethodDiscovery(serviceMethodProviderContext);
            }

            var endpointConventionBuilders = new List<IEndpointConventionBuilder>();
            foreach (var method in serviceMethodProviderContext.Methods)
            {
                var pattern = method.Method.FullName;
                var endpointBuilder = endpointRouteBuilder.MapPost(pattern, method.RequestDelegate);

                endpointBuilder.Add(ep =>
                {
                    ep.DisplayName = $"gRPC - {pattern}";

                    ep.Metadata.Add(new GrpcMethodMetadata(typeof(TService), method.Method));
                    foreach (var item in method.Metadata)
                    {
                        ep.Metadata.Add(item);
                    }
                });

                endpointConventionBuilders.Add(endpointBuilder);

                Log.ServiceMethodAdded(_logger, method.Method.Name, method.Method.ServiceName, method.Method.Type, pattern);
            }

            CreateUnimplementedEndpoints(
                endpointRouteBuilder,
                _serviceMethodsRegistry,
                _serverCallHandlerFactory,
                serviceMethodProviderContext.Methods);

            _serviceMethodsRegistry.Methods.AddRange(serviceMethodProviderContext.Methods);

            return endpointConventionBuilders;
        }

        internal static void CreateUnimplementedEndpoints(
            IEndpointRouteBuilder endpointRouteBuilder,
            ServiceMethodsRegistry serviceMethodsRegistry,
            ServerCallHandlerFactory<TService> serverCallHandlerFactory,
            List<MethodModel> serviceMethods)
        {
            // Return UNIMPLEMENTED status for missing service:
            // - /{service}/{method} + content-type header = grpc/application
            if (serviceMethodsRegistry.Methods.Count == 0)
            {
                // Only one unimplemented service endpoint is needed for the application
                CreateUnimplementedEndpoint(endpointRouteBuilder, "{unimplementedService}/{unimplementedMethod}", "gRPC - Unimplemented service", serverCallHandlerFactory.CreateUnimplementedService());
            }

            // Return UNIMPLEMENTED status for missing method:
            // - /Package.Service/{method} + content-type header = grpc/application
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

                CreateUnimplementedEndpoint(endpointRouteBuilder, serviceName + "/{unimplementedMethod}", $"gRPC - Unimplemented method for {serviceName}", serverCallHandlerFactory.CreateUnimplementedMethod());
            }
        }

        private static void CreateUnimplementedEndpoint(IEndpointRouteBuilder endpointRouteBuilder, string pattern, string displayName, RequestDelegate requestDelegate)
        {
            var routePattern = RoutePatternFactory.Parse(pattern, defaults: null, new { contentType = GrpcContentTypeConstraint.Instance });
            var endpointBuilder = endpointRouteBuilder.Map(routePattern, requestDelegate);

            endpointBuilder.Add(ep =>
            {
                ep.DisplayName = $"gRPC - {displayName}";
                ep.Metadata.Add(new HttpMethodMetadata(new[] { "POST" }));
            });
        }

        private class GrpcContentTypeConstraint : IRouteConstraint
        {
            public static readonly GrpcContentTypeConstraint Instance = new GrpcContentTypeConstraint();

            public bool Match(HttpContext httpContext, IRouter route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection)
            {
                if (httpContext == null)
                {
                    return false;
                }

                return GrpcProtocolHelpers.IsGrpcContentType(httpContext.Request.ContentType);
            }

            private GrpcContentTypeConstraint()
            {
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, string, MethodType, string, Exception?> _serviceMethodAdded =
                LoggerMessage.Define<string, string, MethodType, string>(LogLevel.Debug, new EventId(1, "ServiceMethodAdded"), "Added gRPC method '{MethodName}' to service '{ServiceName}'. Method type: '{MethodType}', route pattern: '{RoutePattern}'.");

            public static void ServiceMethodAdded(ILogger logger, string methodName, string serviceName, MethodType methodType, string routePattern)
            {
                _serviceMethodAdded(logger, methodName, serviceName, methodType, routePattern, null);
            }
        }
    }
}
