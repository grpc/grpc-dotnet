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

using Greet;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttribute;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moq;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests;

[TestFixture]
public class GrpcEndpointRouteBuilderExtensionsTests
{
    [Test]
    public void MapGrpcReflectionService_WithoutServices_RaiseError()
    {
        // Arrange
        var services = new ServiceCollection();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => routeBuilder.MapGrpcReflectionService())!;
        Assert.AreEqual("Unable to find the required services. Please add all the required services by calling " +
                "'IServiceCollection.AddGrpcReflection()' inside the call to 'ConfigureServices(...)' in the application startup code.", ex.Message);
    }

    [Test]
    public void MapGrpcService_WithoutServices_RaiseError()
    {
        // Arrange
        var services = new ServiceCollection();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => routeBuilder.MapGrpcService<Greeter.GreeterBase>())!;
        Assert.AreEqual("Unable to find the required services. Please add all the required services by calling " +
                "'IServiceCollection.AddGrpc' inside the call to 'ConfigureServices(...)' in the application startup code.", ex.Message);
    }

    [Test]
    public void MapGrpcService_CantBind_RaiseError()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() => routeBuilder.MapGrpcService<ErrorService>())!;
        Assert.AreEqual("Error binding gRPC service 'ErrorService'.", ex.Message);
        Assert.AreEqual("Error!", ex.InnerException?.InnerException?.Message);
    }

    [BindServiceMethod(typeof(ErrorService), "BindMethod")]
    private class ErrorService
    {
        public static void BindMethod(ServiceBinderBase binder, ErrorService errorService)
        {
            throw new Exception("Error!");
        }
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
        var services = ServicesHelpers.CreateServices();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<TService>();

        // Assert
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .Where(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() != null)
            .ToList();
        Assert.AreEqual(2, endpoints.Count);

        var routeEndpoint1 = (RouteEndpoint)endpoints[0];
        Assert.AreEqual("/greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
        Assert.AreEqual("POST", routeEndpoint1.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single());
        Assert.AreEqual("/greet.Greeter/SayHello", routeEndpoint1.Metadata.GetMetadata<GrpcMethodMetadata>()?.Method.FullName);

        var routeEndpoint2 = (RouteEndpoint)endpoints[1];
        Assert.AreEqual("/greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
        Assert.AreEqual("POST", routeEndpoint2.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Single());
        Assert.AreEqual("/greet.Greeter/SayHellos", routeEndpoint2.Metadata.GetMetadata<GrpcMethodMetadata>()?.Method.FullName);
    }

    [Test]
    public void MapGrpcService_LoggerAttachedAndMethodsDiscovered_AddsLogForBoundMethod()
    {
        // Arrange
        var testSink = new TestSink();
        var testLogger = new TestLogger(string.Empty, testSink, true);

        var mockLoggerFactory = new Mock<ILoggerFactory>();
        mockLoggerFactory
            .Setup(m => m.CreateLogger(It.IsAny<string>()))
            .Returns((string categoryName) => testLogger);

        var services = ServicesHelpers.CreateServices();
        services.AddSingleton<ILoggerFactory>(mockLoggerFactory.Object);

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<GreeterWithAttributeService>();

        // Assert
        var writes = testSink.Writes.ToList();

        var s1 = writes[0].State.ToString();
        Assert.AreEqual("Discovering gRPC methods for Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttribute.GreeterWithAttributeService.", s1);

        var s2 = writes[1].State.ToString();
        Assert.AreEqual("Added gRPC method 'SayHello' to service 'greet.Greeter'. Method type: Unary, HTTP method: POST, route pattern: '/greet.Greeter/SayHello'.", s2);

        var s3 = writes[2].State.ToString();
        Assert.AreEqual("Added gRPC method 'SayHellos' to service 'greet.Greeter'. Method type: ServerStreaming, HTTP method: POST, route pattern: '/greet.Greeter/SayHellos'.", s3);
    }

    [Test]
    public void MapGrpcService_LoggerAttachedAndNoMethodsDiscovered_AddsWarningLog()
    {
        // Arrange
        var testSink = new TestSink();
        var testLogger = new TestLogger(string.Empty, testSink, true);

        var mockLoggerFactory = new Mock<ILoggerFactory>();
        mockLoggerFactory
            .Setup(m => m.CreateLogger(It.IsAny<string>()))
            .Returns((string categoryName) => testLogger);

        var services = ServicesHelpers.CreateServices();
        services.AddSingleton<ILoggerFactory>(mockLoggerFactory.Object);

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<object>();

        // Assert
        var writes = testSink.Writes.ToList();

        var s1 = writes[0].State.ToString();
        Assert.AreEqual("Discovering gRPC methods for System.Object.", s1);

        var s2 = writes[1].State.ToString();
        Assert.AreEqual("Could not find bind method for System.Object.", s2);

        var s3 = writes[2].State.ToString();
        Assert.AreEqual("No gRPC methods discovered for System.Object.", s3);
    }

    [Test]
    public void MapGrpcService_ConventionBuilder_AddsMetadata()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<GreeterWithAttributeService>().Add(builder =>
        {
            builder.Metadata.Add(new CustomMetadata());
        });

        // Assert
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .ToList();
        Assert.AreEqual(4, endpoints.Count);

        var routeEndpoint1 = (RouteEndpoint)endpoints[0];
        Assert.AreEqual("/greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
        Assert.NotNull(routeEndpoint1.Metadata.GetMetadata<CustomMetadata>());

        var routeEndpoint2 = (RouteEndpoint)endpoints[1];
        Assert.AreEqual("/greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
        Assert.NotNull(routeEndpoint2.Metadata.GetMetadata<CustomMetadata>());

        var routeEndpoint3 = (RouteEndpoint)endpoints[2];
        Assert.AreEqual("{unimplementedService}/{unimplementedMethod:grpcunimplemented}", routeEndpoint3.RoutePattern.RawText);
        Assert.NotNull(routeEndpoint3.Metadata.GetMetadata<CustomMetadata>());

        var routeEndpoint4 = (RouteEndpoint)endpoints[3];
        Assert.AreEqual("greet.Greeter/{unimplementedMethod:grpcunimplemented}", routeEndpoint4.RoutePattern.RawText);
        Assert.NotNull(routeEndpoint4.Metadata.GetMetadata<CustomMetadata>());
    }

    [Test]
    public void MapGrpcService_ServiceWithAttribute_AddsAttributesAsMetadata()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<GreeterServiceWithMetadataAttributes>();

        // Assert
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .Where(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() != null)
            .ToList();
        Assert.AreEqual(2, endpoints.Count);

        var routeEndpoint1 = (RouteEndpoint)endpoints[0];
        Assert.AreEqual("/greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);
        Assert.AreEqual("Method", routeEndpoint1.Metadata.GetMetadata<CustomAttribute>()?.Value);

        var routeEndpoint2 = (RouteEndpoint)endpoints[1];
        Assert.AreEqual("/greet.Greeter/SayHellos", routeEndpoint2.RoutePattern.RawText);
        Assert.AreEqual("Class", routeEndpoint2.Metadata.GetMetadata<CustomAttribute>()?.Value);
    }

    [Test]
    public void MapGrpcService_ServiceWithAttributeAndBuilder_TestMetdataPrecedence()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

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
        Assert.AreEqual("/greet.Greeter/SayHello", routeEndpoint1.RoutePattern.RawText);

        var orderedMetadata = routeEndpoint1.Metadata.GetOrderedMetadata<CustomAttribute>().ToList();
        Assert.AreEqual("Class", orderedMetadata[0].Value);
        Assert.AreEqual("Method", orderedMetadata[1].Value);
        Assert.AreEqual("Builder", orderedMetadata[2].Value);

        Assert.AreEqual("Builder", routeEndpoint1.Metadata.GetMetadata<CustomAttribute>()?.Value);
    }

    [Test]
    public void MapGrpcService_NoMatchingCompressionProvider_ThrowError()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices(options =>
        {
            options.ResponseCompressionAlgorithm = "DOES_NOT_EXIST";
        });

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            routeBuilder.MapGrpcService<GreeterWithAttributeService>();
        })!;

        // Assert
        Assert.AreEqual("Error binding gRPC service 'GreeterWithAttributeService'.", ex.Message);
        Assert.AreEqual("The configured response compression algorithm 'DOES_NOT_EXIST' does not have a matching compression provider.", ex.InnerException!.InnerException!.Message);
    }

    [Test]
    public void MapGrpcService_IgnoreUnknownServicesDefault_RegisterUnknownHandler()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices();

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<GreeterServiceWithMetadataAttributes>();

        // Assert
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .ToList();

        Assert.IsNotNull(endpoints.SingleOrDefault(e => e.DisplayName == "gRPC - Unimplemented service"));
        Assert.IsNotNull(endpoints.SingleOrDefault(e => e.DisplayName == "gRPC - Unimplemented method for greet.Greeter"));
    }

    [Test]
    public void MapGrpcService_IgnoreUnknownServicesGlobalTrue_DontRegisterUnknownHandler()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices(o => o.IgnoreUnknownServices = true);

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<GreeterServiceWithMetadataAttributes>();

        // Assert
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .ToList();

        Assert.IsNull(endpoints.SingleOrDefault(e => e.DisplayName == "gRPC - Unimplemented service"));
        Assert.IsNull(endpoints.SingleOrDefault(e => e.DisplayName == "gRPC - Unimplemented method for greet.Greeter"));

        Assert.AreEqual(0, endpoints.Count(e => e.Metadata.GetMetadata<GrpcMethodMetadata>() == null));
    }

    [Test]
    public void MapGrpcService_IgnoreUnknownServicesServiceTrue_DontRegisterUnknownHandler()
    {
        // Arrange
        var services = ServicesHelpers.CreateServices(configureGrpcService: o =>
        {
            o.AddServiceOptions<GreeterServiceWithMetadataAttributes>(o => o.IgnoreUnknownServices = true);
        });

        var routeBuilder = CreateTestEndpointRouteBuilder(services.BuildServiceProvider(validateScopes: true));

        // Act
        routeBuilder.MapGrpcService<GreeterServiceWithMetadataAttributes>();

        // Assert
        var endpoints = routeBuilder.DataSources
            .SelectMany(ds => ds.Endpoints)
            .ToList();

        Assert.IsNotNull(endpoints.SingleOrDefault(e => e.DisplayName == "gRPC - Unimplemented service"));
        Assert.IsNull(endpoints.SingleOrDefault(e => e.DisplayName == "gRPC - Unimplemented method for GreeterServiceWithMetadataAttributes"));
    }

    public IEndpointRouteBuilder CreateTestEndpointRouteBuilder(IServiceProvider serviceProvider)
    {
        return new TestEndpointRouteBuilder(serviceProvider);
    }
}
