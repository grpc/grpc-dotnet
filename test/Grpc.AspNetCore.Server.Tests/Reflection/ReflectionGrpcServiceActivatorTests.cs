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
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.Core;
using Grpc.Reflection;
using Grpc.Reflection.V1Alpha;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Reflection
{
    [TestFixture]
    public class ReflectionGrpcServiceActivatorTests
    {
        [Test]
        public async Task Create_ConfiguredGrpcEndpoint_EndpointReturnedFromReflectionService()
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
            endpointRouteBuilder.MapGrpcService<GreeterService>();

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

            // Assert
            Assert.AreEqual(1, writer.Responses.Count);
            Assert.AreEqual(1, writer.Responses[0].ListServicesResponse.Service.Count);

            var serviceResponse = writer.Responses[0].ListServicesResponse.Service[0];
            Assert.AreEqual("greet.Greeter", serviceResponse.Name);
        }

        private class GreeterService : Greeter.GreeterBase
        {
        }

        private class TestAsyncStreamReader : IAsyncStreamReader<ServerReflectionRequest>
        {
            // IAsyncStreamReader<T> should declare Current as nullable
            // Suppress warning when overriding interface definition
#pragma warning disable CS8613, CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member.
            public ServerReflectionRequest? Current { get; set; }
#pragma warning restore CS8613, CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member.
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
