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
using FunctionalTestsWebsite.Infrastructure;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Builder.Internal;
using Microsoft.AspNetCore.Routing;
using Moq;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
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

        private static GrpcEndpointModel<TInvoker> CreateModel<TInvoker>(TInvoker invoker) where TInvoker : Delegate
        {
            return new GrpcEndpointModel<TInvoker>(invoker, new List<object>());
        }

        public string AddUnaryMethod<TService, TRequest, TResponse>(UnaryServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
            where TService : class
            where TRequest : class, IMessage, new()
            where TResponse : class, IMessage, new()
        {
            var method = CreateMethod<TService, TRequest, TResponse>(MethodType.Unary, methodName ?? Guid.NewGuid().ToString());

            Mock<IGrpcMethodModelFactory<TService>> mockFactory = new Mock<IGrpcMethodModelFactory<TService>>();
            mockFactory.Setup(m => m.CreateUnaryModel(method)).Returns(() => CreateModel(new UnaryServerMethod<TService, TRequest, TResponse>((service, request, context) => callHandler(request, context))));

            AddServiceCore((binder, _) => binder.AddMethod(method, (UnaryServerMethod<TRequest, TResponse>)null!), mockFactory.Object);

            return method.FullName;
        }

        public string AddServerStreamingMethod<TService, TRequest, TResponse>(ServerStreamingServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
            where TService : class
            where TRequest : class, IMessage, new()
            where TResponse : class, IMessage, new()
        {
            var method = CreateMethod<TService, TRequest, TResponse>(MethodType.ServerStreaming, methodName ?? Guid.NewGuid().ToString());

            Mock<IGrpcMethodModelFactory<TService>> mockFactory = new Mock<IGrpcMethodModelFactory<TService>>();
            mockFactory.Setup(m => m.CreateServerStreamingModel(method)).Returns(() => CreateModel(new ServerStreamingServerMethod<TService, TRequest, TResponse>((service, request, stream, context) => callHandler(request, stream, context))));

            AddServiceCore((binder, _) => binder.AddMethod(method, (ServerStreamingServerMethod<TRequest, TResponse>)null!), mockFactory.Object);

            return method.FullName;
        }

        public string AddClientStreamingMethod<TService, TRequest, TResponse>(ClientStreamingServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
            where TService : class
            where TRequest : class, IMessage, new()
            where TResponse : class, IMessage, new()
        {
            var method = CreateMethod<TService, TRequest, TResponse>(MethodType.ClientStreaming, methodName ?? Guid.NewGuid().ToString());

            Mock<IGrpcMethodModelFactory<TService>> mockFactory = new Mock<IGrpcMethodModelFactory<TService>>();
            mockFactory.Setup(m => m.CreateClientStreamingModel(method)).Returns(() => CreateModel(new ClientStreamingServerMethod<TService, TRequest, TResponse>((service, stream, context) => callHandler(stream, context))));

            AddServiceCore((binder, _) => binder.AddMethod(method, (ClientStreamingServerMethod<TRequest, TResponse>)null!), mockFactory.Object);

            return method.FullName;
        }

        public string AddDuplexStreamingMethod<TService, TRequest, TResponse>(DuplexStreamingServerMethod<TRequest, TResponse> callHandler, string? methodName = null)
            where TService : class
            where TRequest : class, IMessage, new()
            where TResponse : class, IMessage, new()
        {
            var method = CreateMethod<TService, TRequest, TResponse>(MethodType.DuplexStreaming, methodName ?? Guid.NewGuid().ToString());

            Mock<IGrpcMethodModelFactory<TService>> mockFactory = new Mock<IGrpcMethodModelFactory<TService>>();
            mockFactory.Setup(m => m.CreateDuplexStreamingModel(method)).Returns(() => CreateModel(new DuplexStreamingServerMethod<TService, TRequest, TResponse>((service, input, output, context) => callHandler(input, output, context))));

            AddServiceCore((binder, _) => binder.AddMethod(method, (DuplexStreamingServerMethod<TRequest, TResponse>)null!), mockFactory.Object);

            return method.FullName;
        }

        private void AddServiceCore<TService>(Action<ServiceBinderBase, TService?> bindAction, IGrpcMethodModelFactory<TService> modelFactory)
            where TService : class
        {
            var routeBuilder = new DynamicEndpointRouteBuilder(_serviceProvider);
            routeBuilder.MapGrpcService<TService>(options =>
            {
                options.BindAction = bindAction;
                options.ModelFactory = modelFactory;
            });

            var endpoints = routeBuilder.DataSources.SelectMany(ds => ds.Endpoints).ToList();

            _endpointDataSource.AddEndpoints(endpoints);
        }

        private Method<TRequest, TResponse> CreateMethod<TService, TRequest, TResponse>(MethodType methodType, string methodName)
            where TRequest : class, IMessage, new()
            where TResponse : class, IMessage, new()
        {
            return new Method<TRequest, TResponse>(
                methodType,
                typeof(TService).Name,
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
}
