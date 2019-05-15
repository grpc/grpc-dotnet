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
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Server.Interceptors
{
    public class MaxConcurrentCallsInterceptor : Interceptor
    {
        private static readonly RpcException _maxConcurrentCallsExceededException = new RpcException(new Status(StatusCode.ResourceExhausted, "Maximum number of concurrent calls exceeded."), "Maximum number of concurrent calls exceeded.");
        private SemaphoreSlim _concurrentCalls;

        public MaxConcurrentCallsInterceptor(int maxConcurrentCalls)
        {
            if (maxConcurrentCalls <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentCalls), maxConcurrentCalls, $"{nameof(maxConcurrentCalls)} must be a positive number.");
            }

            _concurrentCalls = new SemaphoreSlim(maxConcurrentCalls, maxConcurrentCalls);
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            CheckConcurrentLimit();

            try
            {
                return await continuation(request, context);
            }
            finally
            {
                _concurrentCalls.Release();
            }
        }

        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            CheckConcurrentLimit();

            try
            {
                return await continuation(requestStream, context);
            }
            finally
            {
                _concurrentCalls.Release();
            }
        }

        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
        {
            CheckConcurrentLimit();

            try
            {
                await continuation(request, responseStream, context);
            }
            finally
            {
                _concurrentCalls.Release();
            }
        }

        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            CheckConcurrentLimit();

            try
            {
                await continuation(requestStream, responseStream, context);
            }
            finally
            {
                _concurrentCalls.Release();
            }
        }

        private void CheckConcurrentLimit()
        {
            if (!_concurrentCalls.Wait(0))
            {
                throw _maxConcurrentCallsExceededException;
            }
        }
    }
}
