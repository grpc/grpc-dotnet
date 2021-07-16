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

using System.Net.Http;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Client
{
    [TestFixture]
    public class InterceptorTests : FunctionalTestBase
    {
        [Test]
        public async Task ClientStreams_CreateWithClientFactory_InterceptorCalled()
        {
            async Task<HelloReply> ClientStreamingMethod(IAsyncStreamReader<HelloRequest> request, ServerCallContext context)
            {
                while (await request.MoveNext())
                {
                    // Nom
                }
                return new HelloReply();
            }
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<HelloRequest, HelloReply>(ClientStreamingMethod);

            var invokeCount = 0;
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(new CallbackInterceptor(o => invokeCount++));
            serviceCollection.AddSingleton<ILoggerFactory>(LoggerFactory);
            serviceCollection
                .AddGrpcClient<TestClient<HelloRequest, HelloReply>>(options =>
                {
                    options.Address = Fixture.GetUrl(TestServerEndpointName.Http2WithTls);
                })
                .AddInterceptor<CallbackInterceptor>()
                .ConfigureGrpcClientCreator(invoker =>
                {
                    return TestClientFactory.Create(invoker, method);
                })
                .ConfigurePrimaryHttpMessageHandler(() =>
                {
                    return new HttpClientHandler
                    {
                        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                    };
                });
            var services = serviceCollection.BuildServiceProvider();

            var client = services.GetRequiredService<TestClient<HelloRequest, HelloReply>>();

            // Act
            var call = client.ClientStreamingCall();

            // Assert
            Assert.AreEqual(1, invokeCount);

            await call.RequestStream.CompleteAsync().DefaultTimeout();
            await call.ResponseAsync.DefaultTimeout();
        }
    }
}
