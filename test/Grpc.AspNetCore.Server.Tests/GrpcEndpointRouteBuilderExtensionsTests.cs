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
using Greet;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class GrpcEndpointRouteBuilderExtensionsTests
    {
        [Test]
        public void MapGrpcService_WithoutServices_RaiseError()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => routeBuilder.MapGrpcService<Greeter.GreeterBase>());
            Assert.AreEqual("Unable to find the required services. Please add all the required services by calling " +
                    "'IServiceCollection.AddGrpc' inside the call to 'ConfigureServices(...)' in the application startup code.", ex.Message);
        }

        [Test]
        public void MapGrpcService_CantBind_RaiseError()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act & Assert
            var ex = Assert.Throws<InvalidOperationException>(() => routeBuilder.MapGrpcService<object>());
            Assert.AreEqual("Error binding gRPC service 'Object'.", ex.Message);
            Assert.AreEqual($"Cannot locate BindService(ServiceBinderBase, ServiceBase) method for the current service type: {typeof(object).FullName}.", ex.InnerException.Message);
        }

        [Test]
        public void MapGrpcService_CanBind_CreatesEndpoints()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            routeBuilder.MapGrpcService<GreeterService>();

            // Assert
            var endpoints = routeBuilder.DataSources.SelectMany(ds => ds.Endpoints).ToList();
            Assert.AreEqual(2, endpoints.Count);

            var routeEndpoint1 = (RouteEndpoint)endpoints[0];
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
            Assert.AreEqual("POST", routeEndpoint1.Metadata.GetMetadata<IHttpMethodMetadata>().HttpMethods.Single());

            var routeEndpoint2 = (RouteEndpoint)endpoints[1];
            Assert.AreEqual("/Greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
            Assert.AreEqual("POST", routeEndpoint2.Metadata.GetMetadata<IHttpMethodMetadata>().HttpMethods.Single());
        }

        [Test]
        public void MapGrpcService_ConventionBuilder_AddsMetadata()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            routeBuilder.MapGrpcService<GreeterService>().Add(builder =>
            {
                builder.Metadata.Add(new CustomMetadata());
            });

            // Assert
            var dataSource = routeBuilder.DataSources.Single();
            Assert.AreEqual(2, dataSource.Endpoints.Count);

            var routeEndpoint1 = (RouteEndpoint)dataSource.Endpoints[0];
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
            Assert.NotNull(routeEndpoint1.Metadata.GetMetadata<CustomMetadata>());

            var routeEndpoint2 = (RouteEndpoint)dataSource.Endpoints[1];
            Assert.AreEqual("/Greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
            Assert.NotNull(routeEndpoint2.Metadata.GetMetadata<CustomMetadata>());
        }

        private class GreeterService : Greeter.GreeterBase
        {
        }

        private class CustomMetadata
        {
        }

        private IEndpointRouteBuilder CreateTestEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            return new TestEndpointRouteBuilder
            {
                ServiceProvider = serviceProvider
            };
        }

        private class TestEndpointRouteBuilder : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider { get; set; }

            public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

            public IApplicationBuilder CreateApplicationBuilder()
            {
                throw new NotImplementedException();
            }
        }
    }
}
