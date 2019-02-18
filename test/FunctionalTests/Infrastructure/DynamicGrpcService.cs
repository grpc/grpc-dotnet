﻿#region Copyright notice and license

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
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Builder.Internal;
using Microsoft.AspNetCore.Routing;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class DynamicGrpcService
    {
        private readonly DynamicEndpointDataSource _endpointDataSource;
        private readonly IServiceProvider _serviceProvider;

        public DynamicGrpcService(DynamicEndpointDataSource endpointDataSource, IServiceProvider serviceProvider)
        {
            _endpointDataSource = endpointDataSource;
            _serviceProvider = serviceProvider;
        }

        public string AddUnaryMethod<TService, TRequest, TResponse>(string methodName)
            where TService : class
            where TRequest : class, IMessage, new()
            where TResponse : class, IMessage, new()
        {
            var method = CreateMethod<TService, TRequest, TResponse>(MethodType.Unary, methodName);

            var routeBuilder = new DynamicEndpointRouteBuilder(_serviceProvider);
            routeBuilder.MapGrpcService<TService>(binder =>
            {
                binder.AddMethod(method, (UnaryServerMethod<TRequest, TResponse>)null);
            });

            var endpoints = routeBuilder.DataSources.SelectMany(ds => ds.Endpoints).ToList();

            _endpointDataSource.AddEndpoints(endpoints);

            return method.FullName;
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
            private readonly IServiceProvider _serviceProvider;

            public DynamicEndpointRouteBuilder(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider;
            }

            public IServiceProvider ServiceProvider => _serviceProvider;

            public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

            public IApplicationBuilder CreateApplicationBuilder()
            {
                return new ApplicationBuilder(_serviceProvider);
            }
        }
    }
}
