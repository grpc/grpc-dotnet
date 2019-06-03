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
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests
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
            var url = Fixture.DynamicGrpc.AddUnaryMethod<Empty, Empty>((request, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.FromResult(new Empty());
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new GrpcStreamContent(ms)).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<Empty>();
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_ClientStreaming()
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddClientStreamingMethod<Empty, Empty>((requestStream, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.FromResult(new Empty());
            });

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new GrpcStreamContent(new MemoryStream())).DefaultTimeout();

            // Assert
            var responseMessage = await response.GetSuccessfulGrpcMessageAsync<Empty>();
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_ServerStreaming()
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddServerStreamingMethod<Empty, Empty>((request, responseStream, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.CompletedTask;
            });

            var ms = new MemoryStream();
            MessageHelpers.WriteMessage(ms, new Empty());

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new GrpcStreamContent(ms)).DefaultTimeout();
            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            // Assert
            await MessageHelpers.AssertReadStreamMessageAsync<Empty>(pipeReader);
            response.AssertTrailerStatus();
        }

        [Test]
        public async Task InterceptorsExecutedInRegistrationOrder_AndGlobalInterceptorExecutesFirst_DuplexStreaming()
        {
            // Arrange
            var url = Fixture.DynamicGrpc.AddDuplexStreamingMethod<Empty, Empty>((requestStream, responseStream, context) =>
            {
                var items = context.GetHttpContext().Items;
                Assert.AreEqual(3, items[OrderedInterceptor.OrderHeaderKey]);
                return Task.CompletedTask;
            });

            // Act
            var response = await Fixture.Client.PostAsync(
                url,
                new GrpcStreamContent(new MemoryStream())).DefaultTimeout();
            var responseStream = await response.Content.ReadAsStreamAsync().DefaultTimeout();
            var pipeReader = PipeReader.Create(responseStream);

            // Assert
            await MessageHelpers.AssertReadStreamMessageAsync<Empty>(pipeReader);
            response.AssertTrailerStatus();
        }
    }

    class OrderedInterceptor : Interceptor
    {
        public static readonly string OrderHeaderKey = "Order";
        private int _expectedOrder;

        public OrderedInterceptor(int expectedOrder)
        {
            _expectedOrder = expectedOrder;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            EnsureIncomingOrder(context);
            var result = await continuation(request, context);
            EnsureOutgoingOrder(context);

            return result;
        }

        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            EnsureIncomingOrder(context);
            var result = await continuation(requestStream, context);
            EnsureOutgoingOrder(context);

            return result;
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            EnsureIncomingOrder(context);
            await continuation(request, responseStream, context);
            EnsureOutgoingOrder(context);
        }

        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            EnsureIncomingOrder(context);
            await continuation(requestStream, responseStream, context);
            EnsureOutgoingOrder(context);
        }

        private void EnsureIncomingOrder(ServerCallContext context)
        {
            var items = context.GetHttpContext().Items;

            if (_expectedOrder == 0)
            {
                Assert.IsNull(items[OrderHeaderKey]);
            }
            else
            {
                Assert.AreEqual(_expectedOrder - 1, items[OrderHeaderKey]);
            }

            items[OrderHeaderKey] = _expectedOrder;
        }

        private void EnsureOutgoingOrder(ServerCallContext context)
        {
            var items = context.GetHttpContext().Items;

            Assert.AreEqual(_expectedOrder, items[OrderHeaderKey]);

            if (_expectedOrder == 0)
            {
                items[OrderHeaderKey] = null;
            }
            else
            {
                items[OrderHeaderKey] = _expectedOrder - 1;
            }
        }
    }
}
