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
using Grpc.AspNetCore.Server.GrpcClient;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.HttpClientFactory
{
    [TestFixture]
    public class DefaultGrpcClientFactoryTests
    {
        [Test]
        public void CreateClient_MultipleNamedClients_ReturnMatchingClient()
        {
            // Arrange
            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services);
            services.AddGrpcClient<TestGreeterClient>("contoso", options =>
            {
                options.BaseAddress = new Uri("http://contoso");
            });
            services.AddGrpcClient<TestGreeterClient>("adventureworks", options =>
            {
                options.BaseAddress = new Uri("http://adventureworks");
            });

            var provider = services.BuildServiceProvider();

            // Act
            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            var contosoClient = clientFactory.CreateClient<TestGreeterClient>("contoso");
            var adventureworksClient = clientFactory.CreateClient<TestGreeterClient>("adventureworks");

            // Assert
            Assert.AreEqual("http://contoso", contosoClient.GetCallInvoker().BaseAddress.OriginalString);
            Assert.AreEqual("http://adventureworks", adventureworksClient.GetCallInvoker().BaseAddress.OriginalString);
        }

        [Test]
        public void CreateClient_UnmatchedName_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();
            HttpContextHelpers.SetupHttpContext(services);
            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.BaseAddress = new Uri("http://contoso");
            });

            var provider = services.BuildServiceProvider();

            var clientFactory = provider.GetRequiredService<GrpcClientFactory>();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => clientFactory.CreateClient<TestGreeterClient>("DOES_NOT_EXIST"));

            // Assert
            Assert.AreEqual("No gRPC client configured with name 'DOES_NOT_EXIST'.", ex.Message);
        }
    }
}
