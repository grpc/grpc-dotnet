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
using System.Threading;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.Server.Reflection.Internal;
using Grpc.Core;
using Grpc.Reflection.V1Alpha;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
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
            var services = new ServiceCollection();
            services.AddGrpc();
            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();

            var endpointRouteBuilder = new TestEndpointRouteBuilder(serviceProvider);
            endpointRouteBuilder.MapGrpcService<GreeterService>();

            var dataSource = new CompositeEndpointDataSource(endpointRouteBuilder.DataSources);

            var activator = new ReflectionGrpcServiceActivator(dataSource, NullLoggerFactory.Instance);

            // Act
            var service = activator.Create();

            var reader = new TestAsyncStreamReader
            {
                Current = new ServerReflectionRequest
                {
                    ListServices = "" // list all services
                }
            };
            var writer = new TestServerStreamWriter();
            var context = HttpContextServerCallContextHelper.CreateServerCallContext();

            await service.ServerReflectionInfo(reader, writer, context);

            // Assert
            Assert.AreEqual(1, writer.Responses.Count);
            Assert.AreEqual(1, writer.Responses[0].ListServicesResponse.Service.Count);

            var serviceResponse = writer.Responses[0].ListServicesResponse.Service[0];
            Assert.AreEqual("Greet.Greeter", serviceResponse.Name);
        }

        private class GreeterService : Greeter.GreeterBase
        {
        }

        private class TestServerStreamWriter : IServerStreamWriter<ServerReflectionResponse>
        {
            public WriteOptions? WriteOptions { get; set; }
            public List<ServerReflectionResponse> Responses { get; } = new List<ServerReflectionResponse>();

            public Task WriteAsync(ServerReflectionResponse message)
            {
                Responses.Add(message);
                return Task.CompletedTask;
            }
        }

        private class TestAsyncStreamReader : IAsyncStreamReader<ServerReflectionRequest>
        {
            // IAsyncStreamReader<T> should declare Current as nullable
            // Suppress warning when overriding interface definition
#pragma warning disable CS8612 // Nullability of reference types in type doesn't match implicitly implemented member.
            public ServerReflectionRequest? Current { get; set; }
#pragma warning restore CS8612 // Nullability of reference types in type doesn't match implicitly implemented member.
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
            public TestEndpointRouteBuilder(IServiceProvider serviceProvider)
            {
                DataSources = new List<EndpointDataSource>();
                ServiceProvider = serviceProvider;
            }

            public ICollection<EndpointDataSource> DataSources { get; }
            public IServiceProvider ServiceProvider { get; }

            public IApplicationBuilder CreateApplicationBuilder()
            {
                throw new NotImplementedException();
            }
        }
    }
}
