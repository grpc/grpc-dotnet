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
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Reflection;
using Grpc.Reflection.V1Alpha;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Reflection
{
    [TestFixture]
    public class ReflectionGrpcServiceActivatorTests
    {
        [Test]
        public async Task Create_ConfiguredGrpcEndpoint_EndpointReturnedFromReflectionService()
        {
            // Arrange and act
            TestServerStreamWriter<ServerReflectionResponse> writer = await ConfigureReflectionServerAndCallAsync(builder =>
            {
                builder.MapGrpcService<GreeterService>();
            });

            // Assert
            Assert.AreEqual(1, writer.Responses.Count);
            Assert.AreEqual(1, writer.Responses[0].ListServicesResponse.Service.Count);

            var serviceResponse = writer.Responses[0].ListServicesResponse.Service[0];
            Assert.AreEqual("greet.Greeter", serviceResponse.Name);
        }

        [Test]
        public async Task Create_ConfiguredGrpcEndpointWithMultipleInheritenceLevel_EndpointReturnedFromReflectionService()
        {
            // Arrange and act
            TestServerStreamWriter<ServerReflectionResponse> writer = await ConfigureReflectionServerAndCallAsync(builder =>
            {
                builder.MapGrpcService<InheritGreeterService>();
            });

            // Assert
            Assert.AreEqual(1, writer.Responses.Count);
            Assert.AreEqual(1, writer.Responses[0].ListServicesResponse.Service.Count);

            var serviceResponse = writer.Responses[0].ListServicesResponse.Service[0];
            Assert.AreEqual("greet.Greeter", serviceResponse.Name);
        }

        [Test]
        public async Task Create_ConfiguredGrpcEndpointWithBaseType_EndpointReturnedFromReflectionService()
        {
            // Arrange and act
            TestServerStreamWriter<ServerReflectionResponse> writer = await ConfigureReflectionServerAndCallAsync(builder =>
            {
                builder.MapGrpcService<GreeterServiceWithBaseType>();
            });

            // Assert
            Assert.AreEqual(1, writer.Responses.Count);
            Assert.AreEqual(1, writer.Responses[0].ListServicesResponse.Service.Count);

            var serviceResponse = writer.Responses[0].ListServicesResponse.Service[0];
            Assert.AreEqual("greet.ThirdGreeterWithBaseType", serviceResponse.Name);
        }

        private static async Task<TestServerStreamWriter<ServerReflectionResponse>> ConfigureReflectionServerAndCallAsync(Action<IEndpointRouteBuilder> action)
        {
            // Arrange
            var endpointRouteBuilder = new TestEndpointRouteBuilder();

            var services = ServicesHelpers.CreateServices();
            services.AddGrpcReflection();
            services.AddRouting();
            services.AddSingleton<EndpointDataSource>(s =>
            {
                return new CompositeEndpointDataSource(endpointRouteBuilder.DataSources);
            });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            endpointRouteBuilder.ServiceProvider = serviceProvider;

            action(endpointRouteBuilder);

            // Act
            var service = serviceProvider.GetRequiredService<ReflectionServiceImpl>();

            var reader = new TestAsyncStreamReader
            {
                Current = new ServerReflectionRequest
                {
                    ListServices = "" // list all services
                }
            };
            var writer = new TestServerStreamWriter<ServerReflectionResponse>();
            var context = HttpContextServerCallContextHelper.CreateServerCallContext();

            await service.ServerReflectionInfo(reader, writer, context);

            return writer;
        }

        private class InheritGreeterService : GreeterService
        {
        }

        private class GreeterServiceWithBaseType : ThirdGreeterWithBaseType.ThirdGreeterWithBaseTypeBase
        {

        }

        private class GreeterService : Greeter.GreeterBase
        {
        }

        private class TestAsyncStreamReader : IAsyncStreamReader<ServerReflectionRequest>
        {
            public ServerReflectionRequest Current { get; set; } = default!;
            private bool _hasNext = true;

            public void Dispose()
            {
            }

            public Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                var result = Task.FromResult(_hasNext);
                _hasNext = false;

                return result;
            }
        }

        private class TestEndpointRouteBuilder : IEndpointRouteBuilder
        {
            public TestEndpointRouteBuilder()
            {
                DataSources = new List<EndpointDataSource>();
            }

            public ICollection<EndpointDataSource> DataSources { get; }
            public IServiceProvider ServiceProvider { get; set; } = default!;

            public IApplicationBuilder CreateApplicationBuilder()
            {
                throw new NotImplementedException();
            }
        }
    }
}

namespace Greet
{
    public class ThirdGreeterBaseType
    {

    }

    public static partial class ThirdGreeterWithBaseType
    {
        public partial class ThirdGreeterWithBaseTypeBase : ThirdGreeterBaseType
        {
        }
    }
}
