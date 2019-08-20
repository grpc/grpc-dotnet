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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class GrpcServicesExtensionsTests
    {
        [Test]
        public void AddGrpc_ConfigureOptions_OptionsSet()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpc(o =>
                {
                    o.EnableDetailedErrors = true;
                    o.MaxReceiveMessageSize = 1;
                    o.MaxSendMessageSize = 1;
                });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var options = serviceProvider.GetRequiredService<IOptions<GrpcServiceOptions>>().Value;

            // Assert
            Assert.AreEqual(true, options.EnableDetailedErrors);
            Assert.AreEqual(1, options.MaxReceiveMessageSize);
            Assert.AreEqual(1, options.MaxSendMessageSize);
        }

        [Test]
        public void AddServiceOptions_ConfigureOptions_OverrideGlobalOptions()
        {
            // Arrange
            var services = new ServiceCollection();
            services
                .AddGrpc(o =>
                {
                    o.EnableDetailedErrors = true;
                    o.MaxReceiveMessageSize = 1;
                    o.MaxSendMessageSize = 1;
                })
                .AddServiceOptions<object>(o =>
                {
                    o.MaxSendMessageSize = 2;
                });

            var serviceProvider = services.BuildServiceProvider(validateScopes: true);

            // Act
            var options = serviceProvider.GetRequiredService<IOptions<GrpcServiceOptions<object>>>().Value;

            // Assert
            Assert.AreEqual(true, options.EnableDetailedErrors);
            Assert.AreEqual(1, options.MaxReceiveMessageSize);
            Assert.AreEqual(2, options.MaxSendMessageSize);
        }
    }
}
