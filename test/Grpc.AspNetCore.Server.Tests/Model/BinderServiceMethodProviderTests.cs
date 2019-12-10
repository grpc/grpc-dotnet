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

using System.IO;
using System.Threading.Tasks;
using Greet;
using Grpc.AspNetCore.Server.Internal;
using Grpc.AspNetCore.Server.Model;
using Grpc.AspNetCore.Server.Model.Internal;
using Grpc.AspNetCore.Server.Tests.TestObjects;
using Grpc.Tests.Shared;
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
        public async Task OnServiceMethodDiscovery_ServiceWithDuplicateMethodNames_Success()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddSingleton<GreeterServiceWithDuplicateNames>();

            var serverCallHandlerFactory = new ServerCallHandlerFactory<GreeterServiceWithDuplicateNames>(
                NullLoggerFactory.Instance,
                Options.Create<GrpcServiceOptions>(new GrpcServiceOptions()),
                Options.Create<GrpcServiceOptions<GreeterServiceWithDuplicateNames>>(new GrpcServiceOptions<GreeterServiceWithDuplicateNames>()),
                new TestGrpcServiceActivator<GreeterServiceWithDuplicateNames>());

            var provider = new BinderServiceMethodProvider<GreeterServiceWithDuplicateNames>(NullLoggerFactory.Instance);
            var context = new ServiceMethodProviderContext<GreeterServiceWithDuplicateNames>(serverCallHandlerFactory);

            var httpContext = HttpContextHelpers.CreateContext();
            httpContext.RequestServices = services.BuildServiceProvider();

            // Act
            provider.OnServiceMethodDiscovery(context);

            // Assert
            Assert.AreEqual(2, context.Methods.Count);

            var methodModel = context.Methods[0];
            Assert.AreEqual("SayHello", methodModel.Method.Name);

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new HelloRequest
            {
                Name = "World"
            });
            ms.Seek(0, SeekOrigin.Begin);
            httpContext.Request.Body = ms;

            await methodModel.RequestDelegate(httpContext);

            // Expect 12 (unimplemented) from base type
            Assert.AreEqual("12", httpContext.Response.Headers["grpc-status"]);
        }
    }
}
