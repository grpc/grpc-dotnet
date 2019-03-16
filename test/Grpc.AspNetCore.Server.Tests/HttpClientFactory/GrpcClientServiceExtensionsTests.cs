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
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using Grpc.Core;
using Grpc.NetCore.HttpClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using static Greet.Greeter;

namespace Grpc.AspNetCore.Server.Tests.HttpClientFactory
{
    [TestFixture]
    public class GrpcClientServiceExtensionsTests
    {
        [Test]
        public void UseRequestCancellationTokenIsTrue_NoHttpContext_ThrowError()
        {
            // Arrange
            var services = new ServiceCollection();

            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.UseRequestCancellationToken = true;
            });

            var provider = services.BuildServiceProvider();

            // Act
            var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<TestGreeterClient>());

            // Assert
            Assert.AreEqual("Cannot set the request cancellation token because there is no HttpContext.", ex.Message);
        }

        [Test]
        public void UseRequestCancellationTokenIsTrue_HasHttpContext_UseRequestToken()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var httpContext = new DefaultHttpContext();
            httpContext.RequestAborted = cts.Token;

            var services = new ServiceCollection();
            services.AddSingleton<IHttpContextAccessor>(new TestHttpContextAccessor
            {
                HttpContext = httpContext
            });
            services.AddGrpcClient<TestGreeterClient>(options =>
            {
                options.UseRequestCancellationToken = true;
            });

            var provider = services.BuildServiceProvider();

            // Act
            var client = provider.GetRequiredService<TestGreeterClient>();

            // Assert
            var callInvoker = client.GetCallInvoker();

            Assert.AreEqual(httpContext.RequestAborted, callInvoker.CancellationToken);
        }

        [Test]
        public void MultipleNamedClients()
        {
            // Arrange
            var cts = new CancellationTokenSource();
            var httpContext = new DefaultHttpContext();
            httpContext.RequestAborted = cts.Token;

            var services = new ServiceCollection();
            services.AddSingleton<IHttpContextAccessor>(new TestHttpContextAccessor
            {
                HttpContext = httpContext
            });
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
            var client = provider.GetRequiredService<IHttpClientFactory>();

            client.CreateClient("contoso");

            // Assert
            var callInvoker = client.GetCallInvoker();

            Assert.AreEqual(httpContext.RequestAborted, callInvoker.CancellationToken);
        }


        private class TestGreeterClient : GreeterClient
        {
            private CallInvoker _callInvoker;

            public TestGreeterClient(CallInvoker callInvoker) : base(callInvoker)
            {
                _callInvoker = callInvoker;
            }

            public HttpClientCallInvoker GetCallInvoker()
            {
                return (HttpClientCallInvoker)_callInvoker;
            }
        }

        private class TestHttpContextAccessor : IHttpContextAccessor
        {
            public HttpContext HttpContext { get; set; }
        }
    }
}
