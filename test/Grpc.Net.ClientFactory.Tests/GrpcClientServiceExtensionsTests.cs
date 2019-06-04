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
using System.Security.Cryptography.X509Certificates;
using Greet;
using Grpc.Net.ClientFactory;
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
            var certificate = new X509Certificate2();

            ServiceCollection services = new ServiceCollection();
            services
                .AddGrpcClient<Greeter.GreeterClient>(o =>
                {
                    o.BaseAddress = baseAddress;
                });

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
                });
            services
                .AddGrpcClient<Greeter.GreeterClient>("Second", o =>
                {
                    o.BaseAddress = baseAddress2;
                });

            var serviceProvider = services.BuildServiceProvider();

            // Act
            var optionsMonitor = serviceProvider.GetRequiredService<IOptionsMonitor<GrpcClientFactoryOptions>>();
            var options1 = optionsMonitor.Get("First");
            var options2 = optionsMonitor.Get("Second");

            // Assert
            Assert.AreEqual(baseAddress1, options1.BaseAddress);
            Assert.AreEqual(baseAddress2, options2.BaseAddress);
        }
    }
}
