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
using Grpc.AspNetCore.Server.Model;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Model
{
    [TestFixture]
    public class BinderServiceMethodProviderTests
    {
        [Test]
        public void OnServiceMethodDiscovery_ServiceWithDuplicateMethodNames_Success()
        {
            // Arrange
            var services = new ServiceCollection();

            var serverCallHandlerFactory = new ServerCallHandlerFactory<GreeterServiceWithDuplicateNames>(
                NullLoggerFactory.Instance,
                Options.Create<GrpcServiceOptions>(new GrpcServiceOptions()),
                Options.Create<GrpcServiceOptions<GreeterServiceWithDuplicateNames>>(new GrpcServiceOptions<GreeterServiceWithDuplicateNames>()),
                new TestGrpcServiceActivator<GreeterServiceWithDuplicateNames>(),
                services.BuildServiceProvider());

            var provider = new BinderServiceMethodProvider<GreeterServiceWithDuplicateNames>(NullLoggerFactory.Instance);
            var context = new ServiceMethodProviderContext<GreeterServiceWithDuplicateNames>(serverCallHandlerFactory);

            // Act
            provider.OnServiceMethodDiscovery(context);

            // Assert
            Assert.AreEqual(2, context.Methods.Count);
            Assert.AreEqual("SayHello", context.Methods[0].Method.Name);
            Assert.AreEqual("SayHellos", context.Methods[1].Method.Name);
        }
    }
}
