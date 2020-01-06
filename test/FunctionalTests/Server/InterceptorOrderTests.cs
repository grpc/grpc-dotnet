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
using System.IO.Pipelines;
using System.Threading.Tasks;
using FunctionalTestsWebsite.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.AspNetCore.FunctionalTests.Infrastructure;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Tests.Shared;
using Intercept;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Server
{
    [TestFixture]
    public class InterceptorOrderTests : FunctionalTestBase
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services
                .AddGrpc(options =>
                {
                    options.Interceptors.Add<OrderedInterceptor>(0);
                    options.Interceptors.Add<OrderedInterceptor>(1);
                })
                .AddServiceOptions<DynamicService>(options =>
                {
                    options.Interceptors.Add<OrderedInterceptor>(2);
                    options.Interceptors.Add<OrderedInterceptor>(3);
                });
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_Unary()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddUnaryMethod<Empty, Empty>((request, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.FromResult(new Empty());
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<Empty>().DefaultTimeout();
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_ClientStreaming()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddClientStreamingMethod<Empty, Empty>((requestStream, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.FromResult(new Empty());
            });

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(new MemoryStream())).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<Empty>().DefaultTimeout();
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_ServerStreaming()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddServerStreamingMethod<Empty, Empty>((request, responseStream, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.CompletedTask;
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(ms)).DefaultTimeout();
            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            // Assert
            await MessageHelpers.AssertReadStreamMessageAsync<Empty>(pipeReader).DefaultTimeout();
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_DuplexStreaming()
        {
            // Arrange
            var method = Fixture.DynamicGrpc.AddDuplexStreamingMethod<Empty, Empty>((requestStream, responseStream, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.CompletedTask;
            });

            // Act
            var response = await Fixture.Client.PostAsync(
                method.FullName,
                new GrpcStreamContent(new MemoryStream())).DefaultTimeout();
            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            // Assert
            await MessageHelpers.AssertReadStreamMessageAsync<Empty>(pipeReader).DefaultTimeout();
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_ServiceAttributeInterceptors_Unary()
        {
            // Arrange
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act
            var response = await Fixture.Client.PostAsync(
                "intercept.Interceptor/GetInterceptorOrderNoMethodAttributes",
                new GrpcStreamContent(ms)).DefaultTimeout();
            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            // Assert
            var orderReply = await MessageHelpers.AssertReadStreamMessageAsync<OrderReply>(pipeReader).DefaultTimeout();

            Assert.AreEqual(3, orderReply.Order);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_ServiceAndMethodAttributeInterceptors_Unary()
        {
            // Arrange
            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act
            var response = await Fixture.Client.PostAsync(
                "intercept.Interceptor/GetInterceptorOrderHasMethodAttributes",
                new GrpcStreamContent(ms)).DefaultTimeout();
            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            // Assert
            var orderReply = await MessageHelpers.AssertReadStreamMessageAsync<OrderReply>(pipeReader).DefaultTimeout();

            Assert.AreEqual(5, orderReply.Order);
            response.AssertTrailerStatus();
        }
    }
}
