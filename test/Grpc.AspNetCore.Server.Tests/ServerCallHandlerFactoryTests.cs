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

using Grpc.AspNetCore.Server.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class ServerCallHandlerFactoryTests
    {
        [Test]
        public void CreateMethodOptions_MaxReceiveMessageSizeUnset_DefaultValue()
        {
            // Arrange
            var factory = CreateServerCallHandlerFactory(
                o => { },
                o => { });

            // Act
            var options = factory.CreateMethodOptions();

            // Assert
            Assert.AreEqual(GrpcServiceOptionsSetup.DefaultReceiveMaxMessageSize, options.MaxReceiveMessageSize);
        }

        [Test]
        public void CreateMethodOptions_MaxReceiveMessageSizeGlobalNull_NullValue()
        {
            // Arrange
            var factory = CreateServerCallHandlerFactory(
                o => o.MaxReceiveMessageSize = null,
                o => { });

            // Act
            var options = factory.CreateMethodOptions();

            // Assert
            Assert.AreEqual(null, options.MaxReceiveMessageSize);
        }

        [Test]
        public void CreateMethodOptions_MaxReceiveMessageSizeServiceNull_NullValue()
        {
            // Arrange
            var factory = CreateServerCallHandlerFactory(
                o => { },
                o => o.MaxReceiveMessageSize = null);

            // Act
            var options = factory.CreateMethodOptions();

            // Assert
            Assert.AreEqual(null, options.MaxReceiveMessageSize);
        }

        [Test]
        public void CreateMethodOptions_MaxReceiveMessageSizeGlobalNullWithOverride_UseOverride()
        {
            // Arrange
            var factory = CreateServerCallHandlerFactory(
                o => o.MaxReceiveMessageSize = null,
                o => o.MaxReceiveMessageSize = 1);

            // Act
            var options = factory.CreateMethodOptions();

            // Assert
            Assert.AreEqual(1, options.MaxReceiveMessageSize);
        }

        [Test]
        public void CreateMethodOptions_MaxSendMessageSizeServiceNull_NullValue()
        {
            // Arrange
            var factory = CreateServerCallHandlerFactory(
                o => o.MaxSendMessageSize = 1,
                o => o.MaxSendMessageSize = null);

            // Act
            var options = factory.CreateMethodOptions();

            // Assert
            Assert.AreEqual(null, options.MaxSendMessageSize);
        }

        [Test]
        public void CreateMethodOptions_MaxSendMessageSizeGlobalNullWithOverride_UseOverride()
        {
            // Arrange
            var factory = CreateServerCallHandlerFactory(
                o => o.MaxSendMessageSize = null,
                o => o.MaxSendMessageSize = 1);

            // Act
            var options = factory.CreateMethodOptions();

            // Assert
            Assert.AreEqual(1, options.MaxSendMessageSize);
        }

        private static ServerCallHandlerFactory<object> CreateServerCallHandlerFactory(
            Action<GrpcServiceOptions> globalOptions,
            Action<GrpcServiceOptions<object>> serviceOptions)
        {
            var services = new ServiceCollection();
            services.AddGrpc(globalOptions).AddServiceOptions<object>(serviceOptions);
            services.AddLogging();
            var serviceProvider = services.BuildServiceProvider();

            return serviceProvider.GetRequiredService<ServerCallHandlerFactory<object>>();
        }
    }
}
