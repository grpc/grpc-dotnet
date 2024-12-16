using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Grpc.Core;
using Microsoft.AspNetCore.Routing.Patterns;
using Grpc.AspNetCore.Server.Model.Internal;

namespace Grpc.AspNetCore.Server.Internal;

/// <summary>
/// The service binder to bind <see cref="ServerServiceDefinition"/> into ASP.Net core web application server.
/// </summary>
internal class EndpointServiceBinder : ServiceBinderBase
{
    private readonly ServerCallHandlerFactory _serverCallHandlerFactory;
    private readonly IEndpointRouteBuilder _routeBuilder;
    private readonly ILogger _logger;
    public List<IEndpointConventionBuilder> EndpointConventionBuilders { get; }
    public List<MethodModel> MethodModels { get; }

    public EndpointServiceBinder(
        ServerCallHandlerFactory serverCallHandlerFactory,
        IEndpointRouteBuilder routeBuilder,
        ILoggerFactory loggerFactory)
    {
        _serverCallHandlerFactory = serverCallHandlerFactory;
        _routeBuilder = routeBuilder;
        _logger = loggerFactory.CreateLogger<EndpointServiceBinder>();
        EndpointConventionBuilders = new List<IEndpointConventionBuilder>();
        MethodModels = new List<MethodModel>();
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse>? handler)
    {
        if(handler?.Method.DeclaringType == null)
        {
            throw new InvalidOperationException($"Instance methods are only allowed as server implementation for Grpc.Core.ServerServiceDefinition.");
        }
        var serviceType = handler.Method.DeclaringType;
        var metadata = CreateMetadata(serviceType, handler);
        var callHandler = _serverCallHandlerFactory.CreateUnary(method, handler);
        var pattern = RoutePatternFactory.Parse(method.FullName);
        AddMethod(new MethodModel(method, pattern, metadata, callHandler.HandleCallAsync));
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse>? handler)
    {
        if (handler?.Method.DeclaringType == null)
        {
            throw new InvalidOperationException($"Instance methods are only allowed as server implementation for Grpc.Core.ServerServiceDefinition.");
        }
        var serviceType = handler.Method.DeclaringType;
        var metadata = CreateMetadata(serviceType, handler);
        var callHandler = _serverCallHandlerFactory.CreateClientStreaming(method, handler);
        var pattern = RoutePatternFactory.Parse(method.FullName);
        AddMethod(new MethodModel(method, pattern, metadata, callHandler.HandleCallAsync));
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse>? handler)
    {
        if (handler?.Method.DeclaringType == null)
        {
            throw new InvalidOperationException($"Instance methods are only allowed as server implementation for Grpc.Core.ServerServiceDefinition.");
        }
        var serviceType = handler.Method.DeclaringType;
        var metadata = CreateMetadata(serviceType, handler);
        var callHandler = _serverCallHandlerFactory.CreateServerStreaming(method, handler);
        var pattern = RoutePatternFactory.Parse(method.FullName);
        AddMethod(new MethodModel(method, pattern, metadata, callHandler.HandleCallAsync));
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TRequest, TResponse>? handler)
    {
        if (handler?.Method.DeclaringType == null)
        {
            throw new InvalidOperationException($"Instance methods are only allowed as server implementation for Grpc.Core.ServerServiceDefinition.");
        }
        var serviceType = handler.Method.DeclaringType;
        var metadata = CreateMetadata(serviceType, handler);
        var callHandler = _serverCallHandlerFactory.CreateDuplexStreaming(method, handler);
        var pattern = RoutePatternFactory.Parse(method.FullName);
        AddMethod(new MethodModel(method, pattern, metadata, callHandler.HandleCallAsync));
    }

    private IList<object> CreateMetadata(Type serviceType, Delegate handler)
    {
        var metadata = new List<object>();
        // Add type metadata first so it has a lower priority
        metadata.AddRange(serviceType.GetCustomAttributes(inherit: true));
        // Add method metadata last so it has a higher priority
        metadata.AddRange(handler.Method.GetCustomAttributes(inherit: true));

        // Accepting CORS preflight means gRPC will allow requests with OPTIONS + preflight headers.
        // If CORS middleware hasn't been configured then the request will reach gRPC handler.
        // gRPC will return 405 response and log that CORS has not been configured.
        metadata.Add(new HttpMethodMetadata(new[] { "POST" }, acceptCorsPreflight: true));

        return metadata;
    }

    private void AddMethod(MethodModel method)
    {
        var endpointBuilder = _routeBuilder.Map(method.Pattern, method.RequestDelegate);
        endpointBuilder.Add(ep =>
        {
            ep.DisplayName = $"gRPC - {method.Pattern.RawText}";

            foreach (var item in method.Metadata)
            {
                ep.Metadata.Add(item);
            }
        });
        EndpointConventionBuilders.Add(endpointBuilder);
        MethodModels.Add(method);

        var httpMethod = method.Metadata.OfType<HttpMethodMetadata>().LastOrDefault();

        ServiceRouteBuilderLog.LogAddedServiceMethod(
            _logger,
            method.Method.Name,
            method.Method.ServiceName,
            method.Method.Type,
            httpMethod?.HttpMethods ?? Array.Empty<string>(),
            method.Pattern.RawText ?? string.Empty);
    }
}
