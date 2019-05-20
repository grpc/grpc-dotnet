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
using System.Linq;
using Greet;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttribute;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Moq;
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
            BindServiceCore<GreeterWithAttributeService>();
        }

        [Test]
        public void MapGrpcService_CanBindSubclass_CreatesEndpoints()
        {
            BindServiceCore<GreeterWithAttributeServiceSubClass>();
        }

        [Test]
        public void MapGrpcService_CanBindSubSubclass_CreatesEndpoints()
        {
            BindServiceCore<GreeterWithAttributeServiceSubSubClass>();
        }

        private void BindServiceCore<TService>() where TService : class
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            routeBuilder.MapGrpcService<TService>();

            // Assert
            var endpoints = routeBuilder.DataSources
                .SelectMany(ds => ds.Endpoints)
                .Where(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() != null)
                .ToList();
            Assert.AreEqual(2, endpoints.Count);

            var routeEndpoint1 = (RouteEndpoint)endpoints[0];
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
            Assert.AreEqual("POST", routeEndpoint1.Metadata.GetMetadata<IHttpMethodMetadata>().HttpMethods.Single());
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.Metadata.GetMetadata<GrpcMethodMetadata>().Method.FullName);

            var routeEndpoint2 = (RouteEndpoint)endpoints[1];
            Assert.AreEqual("/Greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
            Assert.AreEqual("POST", routeEndpoint2.Metadata.GetMetadata<IHttpMethodMetadata>().HttpMethods.Single());
            Assert.AreEqual("/Greet.Greeter/SayHellos", routeEndpoint2.Metadata.GetMetadata<GrpcMethodMetadata>().Method.FullName);
        }

        [Test]
        public void MapGrpcService_LoggerAttached_AddsLogForBoundMethod()
        {
            // Arrange
            var testSink = new TestSink();
            var testLogger = new TestLogger(string.Empty, testSink, true);

            var loggerName = "Grpc.AspNetCore.Server.Internal.GrpcServiceBinder";
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory
                .Setup(m => m.CreateLogger(It.IsAny<string>()))
                .Returns((string categoryName) => (categoryName == loggerName) ? (ILogger)testLogger : NullLogger.Instance);

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(mockLoggerFactory.Object);
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            routeBuilder.MapGrpcService<GreeterWithAttributeService>();

            // Assert
            var writes = testSink.Writes.ToList();

            var s1 = writes[0].State.ToString();
            Assert.AreEqual("Added gRPC method 'SayHello' to service 'Greet.Greeter'. Method type: 'Unary', route pattern: '/Greet.Greeter/SayHello'.", s1);

            var s2 = writes[1].State.ToString();
            Assert.AreEqual("Added gRPC method 'SayHellos' to service 'Greet.Greeter'. Method type: 'ServerStreaming', route pattern: '/Greet.Greeter/SayHellos'.", s2);
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
            routeBuilder.MapGrpcService<GreeterWithAttributeService>().Add(builder =>
            {
                builder.Metadata.Add(new CustomMetadata());
            });

            // Assert
            var endpoints = routeBuilder.DataSources
                .SelectMany(ds => ds.Endpoints)
                .Where(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() != null)
                .ToList();
            Assert.AreEqual(2, endpoints.Count);

            var routeEndpoint1 = (RouteEndpoint)endpoints[0];
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
            Assert.NotNull(routeEndpoint1.Metadata.GetMetadata<CustomMetadata>());

            var routeEndpoint2 = (RouteEndpoint)endpoints[1];
            Assert.AreEqual("/Greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
            Assert.NotNull(routeEndpoint2.Metadata.GetMetadata<CustomMetadata>());
        }

        [Test]
        public void MapGrpcService_ServiceWithAttribute_AddsAttributesAsMetadata()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            routeBuilder.MapGrpcService<GreeterServiceWithMetadataAttributes>();

            // Assert
            var endpoints = routeBuilder.DataSources
                .SelectMany(ds => ds.Endpoints)
                .Where(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() != null)
                .ToList();
            Assert.AreEqual(2, endpoints.Count);

            var routeEndpoint1 = (RouteEndpoint)endpoints[0];
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
            Assert.AreEqual("Method", routeEndpoint1.Metadata.GetMetadata<CustomAttribute>().Value);

            var routeEndpoint2 = (RouteEndpoint)endpoints[1];
            Assert.AreEqual("/Greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
            Assert.AreEqual("Class", routeEndpoint2.Metadata.GetMetadata<CustomAttribute>().Value);
        }

        [Test]
        public void MapGrpcService_ServiceWithAttributeAndBuilder_TestMetdataPrecedence()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc();

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            routeBuilder.MapGrpcService<GreeterServiceWithMetadataAttributes>().Add(builder =>
            {
                builder.Metadata.Add(new CustomAttribute("Builder"));
            });

            // Assert
            var endpoints = routeBuilder.DataSources
                .SelectMany(ds => ds.Endpoints)
                .Where(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() != null)
                .ToList();
            Assert.AreEqual(2, endpoints.Count);

            var routeEndpoint1 = (RouteEndpoint)endpoints[0];
            Assert.AreEqual("/Greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);

            var orderedMetadata = routeEndpoint1.Metadata.GetOrderedMetadata<CustomAttribute>().ToList();
            Assert.AreEqual("Class", orderedMetadata[0].Value);
            Assert.AreEqual("Method", orderedMetadata[1].Value);
            Assert.AreEqual("Builder", orderedMetadata[2].Value);

            Assert.AreEqual("Builder", routeEndpoint1.Metadata.GetMetadata<CustomAttribute>().Value);
        }

        [Test]
        public void MapGrpcService_NoMatchingCompressionProvider_ThrowError()
        {
            // Arrange
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddGrpc(options =>
            {
                options.ResponseCompressionAlgorithm = "DOES_NOT_EXIST";
            });

            var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider());

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                routeBuilder.MapGrpcService<GreeterWithAttributeService>();
            });

            // Assert
            Assert.AreEqual("The configured response compression algorithm 'DOES_NOT_EXIST' does not have a matching compression provider.", ex.Message);
        }

        public IEndpointRouteBuilder CreateTestEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            return new TestEndpointRouteBuilder(serviceProvider);
        }
    }
}
