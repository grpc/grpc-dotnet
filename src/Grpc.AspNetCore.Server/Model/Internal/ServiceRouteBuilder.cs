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
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Log = Grpc.AspNetCore.Server.Model.Internal.ServiceRouteBuilderLog;

namespace Grpc.AspNetCore.Server.Model.Internal;

internal sealed class ServiceRouteBuilder<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService> where TService : class
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

                // Report the last HttpMethodMetadata added. It's the metadata used by routing.
                var httpMethod = method.Metadata.OfType<HttpMethodMetadata>().LastOrDefault();

                Log.AddedServiceMethod(
                    _logger,
                    method.Method.Name,
                    method.Method.ServiceName,
                    method.Method.Type,
                    httpMethod?.HttpMethods ?? Array.Empty<string>(),
                    method.Pattern.RawText ?? string.Empty);
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
            endpointConventionBuilders.Add(CreateUnimplementedEndpoint(endpointRouteBuilder, $"{{unimplementedService}}/{{unimplementedMethod:{GrpcServerConstants.GrpcUnimplementedConstraintPrefix}}}", "Unimplemented service", serverCallHandlerFactory.CreateUnimplementedService()));
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

                endpointConventionBuilders.Add(CreateUnimplementedEndpoint(endpointRouteBuilder, $"{serviceName}/{{unimplementedMethod:{GrpcServerConstants.GrpcUnimplementedConstraintPrefix}}}", $"Unimplemented method for {serviceName}", serverCallHandlerFactory.CreateUnimplementedMethod()));
            }
        }
    }

    private static IEndpointConventionBuilder CreateUnimplementedEndpoint(IEndpointRouteBuilder endpointRouteBuilder, string pattern, string displayName, RequestDelegate requestDelegate)
    {
        var endpointBuilder = endpointRouteBuilder.Map(pattern, requestDelegate);

        endpointBuilder.Add(ep =>
        {
            ep.DisplayName = $"gRPC - {displayName}";
            // Don't add POST metadata here. It will return 405 status for other HTTP methods which isn't
            // what we want. That check is made in a constraint instead.
        });

        return endpointBuilder;
    }
}

internal static partial class ServiceRouteBuilderLog
{
    [LoggerMessage(Level = LogLevel.Trace, EventId = 1, EventName = "AddedServiceMethod", Message = "Added gRPC method '{MethodName}' to service '{ServiceName}'. Method type: {MethodType}, HTTP method: {HttpMethod}, route pattern: '{RoutePattern}'.")]
    private static partial void AddedServiceMethod(ILogger logger, string methodName, string serviceName, MethodType methodType, string HttpMethod, string routePattern);

    public static void AddedServiceMethod(ILogger logger, string methodName, string serviceName, MethodType methodType, IReadOnlyList<string> httpMethods, string routePattern)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            // There should be one HTTP method here, but concat in case someone has overriden metadata.
            var allHttpMethods = string.Join(',', httpMethods);

            AddedServiceMethod(logger, methodName, serviceName, methodType, allHttpMethods, routePattern);
        }
    }

    [LoggerMessage(Level = LogLevel.Trace, EventId = 2, EventName = "DiscoveringServiceMethods", Message = "Discovering gRPC methods for {ServiceType}.")]
    public static partial void DiscoveringServiceMethods(ILogger logger, Type serviceType);

    [LoggerMessage(Level = LogLevel.Debug, EventId = 3, EventName = "NoServiceMethodsDiscovered", Message = "No gRPC methods discovered for {ServiceType}.")]
    public static partial void NoServiceMethodsDiscovered(ILogger logger, Type serviceType);
}
