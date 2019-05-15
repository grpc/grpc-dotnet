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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Server.Interceptors
{
    public class MaxStreamingRequestTimeoutInterceptor : Interceptor
    {
        private static readonly RpcException _maxStreamingRequestTimeoutExceededException = new RpcException(new Status(StatusCode.Aborted, "Timeout for receiving requests exceeded."), "Timeout for receiving requests exceeded.");

        private readonly TimeSpan _streamingRequestTimeout;

        public MaxStreamingRequestTimeoutInterceptor(TimeSpan streamingRequestTimeout)
        {
            if (streamingRequestTimeout < TimeSpan.Zero && streamingRequestTimeout != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(nameof(streamingRequestTimeout), streamingRequestTimeout, $"{nameof(streamingRequestTimeout)} must be a positive value.");
            }

            _streamingRequestTimeout = streamingRequestTimeout != Timeout.InfiniteTimeSpan ? streamingRequestTimeout : TimeSpan.MaxValue;
        }

        public override Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, ServerCallContext context, ClientStreamingServerMethod<TRequest, TResponse> continuation)
        {
            return continuation(new TimeoutAsyncStreamReader<TRequest>(requestStream, _streamingRequestTimeout), context);
        }

        public override Task DuplexStreamingServerHandler<TRequest, TResponse>(IAsyncStreamReader<TRequest> requestStream, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, DuplexStreamingServerMethod<TRequest, TResponse> continuation)
        {
            return continuation(new TimeoutAsyncStreamReader<TRequest>(requestStream, _streamingRequestTimeout), responseStream, context);
        }

        private class TimeoutAsyncStreamReader<TRequest> : IAsyncStreamReader<TRequest>
        {
            private readonly IAsyncStreamReader<TRequest> _inner;
            private readonly TimeSpan _timeout;

            public TimeoutAsyncStreamReader(IAsyncStreamReader<TRequest> inner, TimeSpan timeout)
            {
                _inner = inner;
                _timeout = timeout;
            }

            public TRequest Current => _inner.Current;

            public void Dispose()
            {
            }

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                var task = _inner.MoveNext(cancellationToken);

                if (task.IsCompleted || Debugger.IsAttached)
                {
                    return await task;
                }

                var cts = new CancellationTokenSource();
                if (task == await Task.WhenAny(task, Task.Delay(_timeout, cts.Token)))
                {
                    cts.Cancel();
                    return await task;
                }
                else
                {
                    throw _maxStreamingRequestTimeoutExceededException;
                }
            }
        }
    }
}
