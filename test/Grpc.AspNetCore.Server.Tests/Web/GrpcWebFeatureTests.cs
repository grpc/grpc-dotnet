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

using Grpc.AspNetCore.Web.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class GrpcWebFeatureTests
    {
        [Test]
        public void Ctor_DefaultHttpContext_FeaturesSet()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();

            // Act
            var feature = CreateFeature(httpContext);

            // Assert
            Assert.AreEqual(feature, httpContext.Features.Get<IHttpResponseBodyFeature>());
            Assert.AreEqual(feature, httpContext.Features.Get<IRequestBodyPipeFeature>());
            Assert.AreEqual(feature, httpContext.Features.Get<IHttpResponseTrailersFeature>());
            Assert.AreEqual(feature, httpContext.Features.Get<IHttpResetFeature>());
        }

        [Test]
        public void DetachFromContext_InitialHttpContext_FeaturesReset()
        {
            // Arrange
            var httpContext = new DefaultHttpContext();
            var responseBodyFeature = httpContext.Features.Get<IHttpResponseBodyFeature>();
            var feature = CreateFeature(httpContext);

            // Act
            feature.DetachFromContext(httpContext);

            // Assert
            Assert.AreEqual(responseBodyFeature, httpContext.Features.Get<IHttpResponseBodyFeature>());
            Assert.AreEqual(null, httpContext.Features.Get<IRequestBodyPipeFeature>());
            Assert.AreEqual(null, httpContext.Features.Get<IHttpResponseTrailersFeature>());
            Assert.AreEqual(null, httpContext.Features.Get<IHttpResetFeature>());
        }

        private static GrpcWebFeature CreateFeature(DefaultHttpContext httpContext)
        {
            return new GrpcWebFeature(
                new ServerGrpcWebContext(ServerGrpcWebMode.GrpcWeb, ServerGrpcWebMode.GrpcWeb),
                httpContext);
        }
    }
}
