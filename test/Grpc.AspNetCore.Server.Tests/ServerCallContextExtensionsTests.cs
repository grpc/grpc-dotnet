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
using Grpc.Core;
using Grpc.Tests.Shared;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests
{
    [TestFixture]
    public class ServerCallContextExtensionsTests
    {
        [Test]
        public void GetHttpContext_HttpContextServerCallContext_Success()
        {
            var httpContext = new DefaultHttpContext();

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(httpContext);

            Assert.AreEqual(httpContext, serverCallContext.GetHttpContext());
        }

        [Test]
        public void GetHttpContext_CustomServerCallContext_Error()
        {
            var serverCallContext = new TestServerCallContext(DateTime.MinValue, CancellationToken.None);

            var ex = Assert.Throws<InvalidOperationException>(() => serverCallContext.GetHttpContext())!;
            Assert.AreEqual("Could not get HttpContext from ServerCallContext. HttpContext can only be accessed when gRPC services are hosted by ASP.NET Core.", ex.Message);
        }

        [Test]
        public void GetHttpContext_CustomServerCallContextWithContextInUserState_Success()
        {
            var httpContext = new DefaultHttpContext();

            var serverCallContext = new TestServerCallContext(DateTime.MinValue, CancellationToken.None);
            serverCallContext.UserState[ServerCallContextExtensions.HttpContextKey] = httpContext;

            Assert.AreEqual(httpContext, serverCallContext.GetHttpContext());
        }
    }
}
