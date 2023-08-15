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

using FunctionalTestsWebsite.Infrastructure;
using Google.Protobuf;
using Grpc.AspNetCore.Server.Model;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure;

/// <summary>
/// Used by tests to add new service methods.
/// </summary>
public class DynamicGrpcServiceRegistry
{
    private readonly DynamicEndpointDataSource _endpointDataSource;
    private readonly IServiceProvider _serviceProvider;

    public DynamicGrpcServiceRegistry(DynamicEndpointDataSource endpointDataSource, IServiceProvider serviceProvider)
    {
        _endpointDataSource = endpointDataSource;
        _serviceProvider = serviceProvider;
    }

    public Method<TRequest, TResponse> AddUnaryMethod<TRequest, TResponse>(UnaryServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        var method = CreateMethod<TRequest, TResponse>(MethodType.Unary, methodName ?? Guid.NewGuid().ToString());

        AddServiceCore(c =>
        {
            c.AddUnaryMethod(method, CreateMetadata(), new UnaryServerMethod<DynamicService, TRequest, TResponse>((service, request, context) => callHandler(request, context)));
        });

        return method;
    }

    public Method<TRequest, TResponse> AddServerStreamingMethod<TRequest, TResponse>(ServerStreamingServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        var method = CreateMethod<TRequest, TResponse>(MethodType.ServerStreaming, methodName ?? Guid.NewGuid().ToString());

        AddServiceCore(c =>
        {
            c.AddServerStreamingMethod(method, CreateMetadata(), new ServerStreamingServerMethod<DynamicService, TRequest, TResponse>((service, request, stream, context) => callHandler(request, stream, context)));
        });

        return method;
    }

    public Method<TRequest, TResponse> AddClientStreamingMethod<TRequest, TResponse>(ClientStreamingServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        var method = CreateMethod<TRequest, TResponse>(MethodType.ClientStreaming, methodName ?? Guid.NewGuid().ToString());

        AddServiceCore(c =>
        {
            c.AddClientStreamingMethod(method, CreateMetadata(), new ClientStreamingServerMethod<DynamicService, TRequest, TResponse>((service, stream, context) => callHandler(stream, context)));
        });

        return method;
    }

    public Method<TRequest, TResponse> AddDuplexStreamingMethod<TRequest, TResponse>(DuplexStreamingServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        var method = CreateMethod<TRequest, TResponse>(MethodType.DuplexStreaming, methodName ?? Guid.NewGuid().ToString());

        AddServiceCore(c =>
        {
            c.AddDuplexStreamingMethod(method, CreateMetadata(), new DuplexStreamingServerMethod<DynamicService, TRequest, TResponse>((service, input, output, context) => callHandler(input, output, context)));
        });

        return method;
    }

    private static List<object> CreateMetadata()
    {
        var metadata = new List<object>
        {
            new HttpMethodMetadata(new[] { "POST" }, acceptCorsPreflight: true)
        };
        return metadata;
    }

    private void AddServiceCore(Action<ServiceMethodProviderContext<DynamicService>> action)
    {
        // Set action for adding dynamic method
        var serviceMethodProviders = _serviceProvider.GetServices<IServiceMethodProvider<DynamicService>>().ToList();
        var dynamicServiceModelProvider = serviceMethodProviders.OfType<DynamicServiceModelProvider>().Single();
        dynamicServiceModelProvider.CreateMethod = action;

        // Add to dynamic endpoint route builder
        var routeBuilder = new DynamicEndpointRouteBuilder(_serviceProvider);
        routeBuilder.MapGrpcService<DynamicService>();

        var endpoints = routeBuilder.DataSources.SelectMany(ds => ds.Endpoints).ToList();
        _endpointDataSource.AddEndpoints(endpoints);
    }

    private Method<TRequest, TResponse> CreateMethod<TRequest, TResponse>(MethodType methodType, string methodName)
        where TRequest : class, IMessage, new()
        where TResponse : class, IMessage, new()
    {
        return new Method<TRequest, TResponse>(
            methodType,
            typeof(DynamicService).Name,
            methodName,
            CreateMarshaller<TRequest>(),
            CreateMarshaller<TResponse>());
    }

    private Marshaller<TMessage> CreateMarshaller<TMessage>()
          where TMessage : class, IMessage, new()
    {
        return new Marshaller<TMessage>(
            m => m.ToByteArray(),
            d =>
            {
                var m = new TMessage();
                m.MergeFrom(d);
                return m;
            });
    }

    private class DynamicEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public DynamicEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder()
        {
            return new ApplicationBuilder(ServiceProvider);
        }
    }
}
