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
using System.Net.Http;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class TelemetryTests : FunctionalTestBase
    {
        [TestCase(ClientType.Channel)]
        [TestCase(ClientType.ClientFactory)]
        public async Task InternalHandler_UnaryCall_TelemetryHeaderSentWithRequest(ClientType clientType)
        {
            await TestTelemetryHeaderIsSet(clientType, handler: null);
        }

#if NET5_0
        [TestCase(ClientType.Channel)]
        [TestCase(ClientType.ClientFactory)]
        public async Task Channel_SocketsHttpHandler_UnaryCall_TelemetryHeaderSentWithRequest(ClientType clientType)
        {
            await TestTelemetryHeaderIsSet(clientType, handler: new SocketsHttpHandler());
        }

        [TestCase(ClientType.Channel)]
        [TestCase(ClientType.ClientFactory)]
        public async Task Channel_SocketsHttpHandlerWrapped_UnaryCall_TelemetryHeaderSentWithRequest(ClientType clientType)
        {
            await TestTelemetryHeaderIsSet(clientType, handler: new TestDelegatingHandler(new SocketsHttpHandler()));
        }

        private class TestDelegatingHandler : DelegatingHandler
        {
            public TestDelegatingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
            {
            }
        }
#endif

        private async Task TestTelemetryHeaderIsSet(ClientType clientType, HttpMessageHandler? handler)
        {
            string? telemetryHeader = null;
            Task<HelloReply> UnaryTelemetryHeader(HelloRequest request, ServerCallContext context)
            {
                telemetryHeader = context.RequestHeaders.GetValue(
#if NET5_0
                    "traceparent"
#else
                    "request-id"
#endif
                    );

                return Task.FromResult(new HelloReply());
            }

            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<HelloRequest, HelloReply>(UnaryTelemetryHeader);
            var client = CreateClient(clientType, method, handler);

            // Act
            await client.UnaryCall(new HelloRequest());

            // Assert
            Assert.IsNotNull(telemetryHeader);
        }

        private TestClient<HelloRequest, HelloReply> CreateClient(ClientType clientType, Method<HelloRequest, HelloReply> method, HttpMessageHandler? handler)
        {
            switch (clientType)
            {
                case ClientType.Channel:
                    {
                        var options = new GrpcChannelOptions
                        {
                            LoggerFactory = LoggerFactory,
                            HttpHandler = handler
                        };

                        // Want to test the behavior of the default, internally created handler.
                        // Only supply the URL to a manually created GrpcChannel.
                        var channel = GrpcChannel.ForAddress(Fixture.GetUrl(TestServerEndpointName.Http2), options);
                        return TestClientFactory.Create(channel, method);
                    }
                case ClientType.ClientFactory:
                    {
                        var serviceCollection = new ServiceCollection();
                        serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
                        serviceCollection
                            .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
                            {
                                options.Address = Fixture.GetUrl(TestServerEndpointName.Http2);
                            })
                            .ConfigureGrpcClientCreator(invoker =>
                            {
                                return TestClientFactory.Create(invoker, method);
                            });
                        var services = serviceCollection.BuildServiceProvider();

                        return services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();
                    }
                default:
                    throw new InvalidOperationException("Unexpected value.");
            }
        }

        public enum ClientType
        {
            Channel,
            ClientFactory
        }
    }
}
