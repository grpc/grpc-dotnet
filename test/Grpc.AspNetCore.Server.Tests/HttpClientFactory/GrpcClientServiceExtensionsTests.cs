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
using System.Threading;
using Grpc.AspNetCore.Server.GrpcClient;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HttpClientFactory
{
    [TestFixture]
    public class GrpcClientServiceExtensionsTests
    {
        [Test]
        public void UseRequestCancellationTokenIsTrue_NoHttpContext_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.PropagateCancellationToken = true;
            });

            var provider = services.BuildServiceProvider();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<TestGreeterClient>());

            // Assert
            Assert.AreEqual("Cannot propagate the call cancellation token to the client. Cannot find the current gRPC ServerCallContext.", ex.Message);
        }

        [Test]
        public void UseRequestCancellationTokenIsTrue_HasHttpContext_UseRequestToken()
        {
            // Arrange
            var cts = new CancellationTokenSource();

            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services, cts.Token);
            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.PropagateCancellationToken = true;
            });

            var provider = services.BuildServiceProvider();

            // Act
            var client = provider.GetRequiredService<TestGreeterClient>();

            // Assert
            Assert.AreEqual(cts.Token, client.GetCallInvoker().CancellationToken);
        }

        [Test]
        public void ResolveDefaultAndNamedClients_ClientsUseCorrectConfiguration()
        {
            // Arrange
            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services);
            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.BaseAddress = new Uri("http://testgreeterclient");
            });
            services.AddGrpcClient<TestSecondGreeterClient>("contoso", options =>
            {
                options.BaseAddress = new Uri("http://contoso");
            });
            services.AddGrpcClient<TestSecondGreeterClient>(options =>
            {
                options.BaseAddress = new Uri("http://testsecondgreeterclient");
            });

            var provider = services.BuildServiceProvider();

            // Act
            var client = provider.GetRequiredService<TestGreeterClient>();
            var secondClient = provider.GetRequiredService<TestSecondGreeterClient>();

            var factory = provider.GetRequiredService<GrpcClientFactory>();
            var contosoClient = factory.CreateClient<TestSecondGreeterClient>("contoso");

            // Assert
            Assert.AreEqual("http://testgreeterclient", client.GetCallInvoker().BaseAddress.OriginalString);
            Assert.AreEqual("http://testsecondgreeterclient", secondClient.GetCallInvoker().BaseAddress.OriginalString);
            Assert.AreEqual("http://contoso", contosoClient.GetCallInvoker().BaseAddress.OriginalString);
        }
    }
}
