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
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Tests.Infrastructure;
using Grpc.AspNetCore.Web;
using Grpc.AspNetCore.Web.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class GrpcWebApplicationBuilderExtensionsTests
    {
        [Test]
        public void UseGrpcWeb_NoServices_Success()
        {
            // Arrange
            var services = new ServiceCollection();
            var app = new ApplicationBuilder(services.BuildServiceProvider());

            // Act & Assert
            app.UseGrpcWeb();
        }

        [Test]
        public async Task UseGrpcWeb_CalledWithMatchingHttpContext_MiddlewareRuns()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            var app = new ApplicationBuilder(services.BuildServiceProvider());

            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

            var appFunc = app.Build();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = GrpcWebProtocolConstants.GrpcWebContentType;

            // Act
            await appFunc(httpContext);

            // Assert
            Assert.AreEqual(GrpcWebProtocolConstants.GrpcContentType, httpContext.Request.ContentType);
        }

        [Test]
        public async Task UseGrpcWeb_RegisteredMultipleTimesCalledWithMatchingHttpContext_MiddlewareRuns()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddLogging();
            var app = new ApplicationBuilder(services.BuildServiceProvider());

            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
            app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });

            var appFunc = app.Build();

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = HttpMethods.Post;
            httpContext.Request.ContentType = GrpcWebProtocolConstants.GrpcWebContentType;

            var testHttpResponseFeature = new TestHttpResponseFeature();
            httpContext.Features.Set<IHttpResponseFeature>(testHttpResponseFeature);

            // Act
            await appFunc(httpContext);

            // Assert
            Assert.AreEqual(GrpcWebProtocolConstants.GrpcContentType, httpContext.Request.ContentType);
            Assert.AreEqual(1, testHttpResponseFeature.StartingCallbackCount);
        }
    }
}
