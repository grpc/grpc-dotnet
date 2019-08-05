﻿#region Copyright notice and license

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
using System.Security.Cryptography.X509Certificates;
using Greet;
using Grpc.Net.ClientFactory;
using Grpc.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.ClientFactory.Tests
{
    [TestFixture]
    public class GrpcClientServiceExtensionsTests
    {
        [Test]
        public void AddGrpcClient_ConfigureOptions_OptionsSet()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            ServiceCollection services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = baseAddress;
                })
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
            var options = optionsMonitor.Get(nameof(Greeter.GreeterClient));

            // Assert
            Assert.AreEqual(baseAddress, options.BaseAddress);
        }

        [Test]
        public void AddGrpcClient_ConfigureNamedOptions_OptionsSet()
        {
            // Arrange
            var baseAddress1 = new Uri("http://localhost");
            var baseAddress2 = new Uri("http://contoso");

            ServiceCollection services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>("First", o =>
                {
                    o.BaseAddress = baseAddress1;
                })
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));
            services
                .AddGrpcClient<Greeter.GreeterClient>("Second", o =>
                {
                    o.BaseAddress = baseAddress2;
                })
                .AddHttpMessageHandler(() => ClientTestHelpers.CreateTestMessageHandler(new HelloReply()));

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
            var options1 = optionsMonitor.Get("First");
            var options2 = optionsMonitor.Get("Second");

            // Assert
            Assert.AreEqual(baseAddress1, options1.BaseAddress);
            Assert.AreEqual(baseAddress2, options2.BaseAddress);
        }

        [Test]
        public void AddGrpcClient_AddsClientBaseClient_Succeeds()
        {
            // Arrange
            var baseAddress = new Uri("http://localhost");

            var services = new ServiceCollection();
            services.AddGrpcClient<Greeter.GreeterClient>(o =>
            {
                o.BaseAddress = baseAddress;
            });

            // Act
            var serviceProvider = services.BuildServiceProvider();

            // Assert
            using (var scope = serviceProvider.CreateScope())
            {
                Assert.IsNotNull(serviceProvider.GetRequiredService<Greeter.GreeterClient>());
            }
        }

        [Test]
        public void AddGrpcClient_AddSameClientTwice_MergeConfiguration()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(options =>
                {
                    options.BaseAddress = new Uri("http://contoso");
                });
            services
                .AddGrpcClient<Greeter.GreeterClient>(options =>
                {
                    options.Interceptors.Add(new CallbackInterceptor(o => { }));
                });

            // Act
            var serviceProvider = services.BuildServiceProvider();
            using (var scope = serviceProvider.CreateScope())
            {
                var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
                var options = optionsMonitor.Get(nameof(Greeter.GreeterClient));

                Assert.AreEqual("http://contoso", options.BaseAddress!.OriginalString);
                Assert.AreEqual(1, options.Interceptors.Count);
            }
        }

        [Test]
        public void AddGrpcClient_AddDifferentClientsWithSameName_ThrowsError()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddGrpcClient<Greeter.GreeterClient>(options => { });

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => services.AddGrpcClient<GreeterClient>(o => { }));

            // Assert
            Assert.AreEqual(
                "The gRPC client factory already has a registered client with the name 'GreeterClient', bound to the type 'Greet.Greeter+GreeterClient'. " +
                "Client names are computed based on the type name without considering the namespace ('GreeterClient'). Use an overload of AddGrpcClient that " +
                "accepts a string and provide a unique name to resolve the conflict.",
                ex.Message);
        }

        private class GreeterClient : Greeter.GreeterClient
        {
        }
    }
}
