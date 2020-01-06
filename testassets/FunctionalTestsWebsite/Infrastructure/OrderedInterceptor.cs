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

using Grpc.Core;
using Grpc.Core.Interceptors;
using System;
using System.Threading.Tasks;

namespace FunctionalTestsWebsite.Infrastructure
{
    public class OrderedInterceptor : Interceptor
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
                AssertValue(null, items[OrderHeaderKey]);
            }
            else
            {
                AssertValue(_expectedOrder - 1, items[OrderHeaderKey]);
            }

            items[OrderHeaderKey] = _expectedOrder;
        }

        private void EnsureOutgoingOrder(ServerCallContext context)
        {
            var items = context.GetHttpContext().Items;

            AssertValue(_expectedOrder, items[OrderHeaderKey]);

            if (_expectedOrder == 0)
            {
                items[OrderHeaderKey] = null;
            }
            else
            {
                items[OrderHeaderKey] = _expectedOrder - 1;
            }
        }

        private void AssertValue(object? expectedValue, object? actualValue)
        {
            if (!Equals(expectedValue, actualValue))
            {
                throw new InvalidOperationException($"Expected {expectedValue}, got {actualValue}.");
            }
        }
    }
}