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

using FunctionalTestsWebsite.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Intercept;
using System;
using System.Threading.Tasks;

namespace FunctionalTestsWebsite.Services
{
    [Interceptor(typeof(OrderedInterceptor), 2)]
    [Interceptor(typeof(OrderedInterceptor), 3)]
    public class InterceptorService : Interceptor.InterceptorBase
    {
        private static readonly string OrderHeaderKey = "Order";

        [Interceptor(typeof(OrderedInterceptor), 4)]
        [Interceptor(typeof(OrderedInterceptor), 5)]
        public override Task<OrderReply> GetInterceptorOrderHasMethodAttributes(Empty request, ServerCallContext context)
        {
            return GetInterceptorOrderCore(context);
        }

        public override Task<OrderReply> GetInterceptorOrderNoMethodAttributes(Empty request, ServerCallContext context)
        {
            return GetInterceptorOrderCore(context);
        }

        private static Task<OrderReply> GetInterceptorOrderCore(ServerCallContext context)
        {
            var items = context.GetHttpContext().Items;
            return Task.FromResult(new OrderReply { Order = Convert.ToInt32(items[OrderHeaderKey]) });
        }
    }
}