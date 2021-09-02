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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

#if NETSTANDARD2_0
using ValueTask = System.Threading.Tasks.Task;
#endif

namespace Grpc.Net.Client.Internal.Retry
{
    internal sealed class StatusGrpcCall<TRequest, TResponse> : IGrpcCall<TRequest, TResponse>
        where TRequest : class
        where TResponse : class
    {
        private readonly Status _status;
        private IClientStreamWriter<TRequest>? _clientStreamWriter;
        private IAsyncStreamReader<TResponse>? _clientStreamReader;

        public IClientStreamWriter<TRequest>? ClientStreamWriter => _clientStreamWriter ??= new StatusClientStreamWriter(_status);
        public IAsyncStreamReader<TResponse>? ClientStreamReader => _clientStreamReader ??= new StatusStreamReader(_status);
        public bool Disposed => true;

        public StatusGrpcCall(Status status)
        {
            _status = status;
        }

        public void Dispose()
        {
        }

        public Task<TResponse> GetResponseAsync()
        {
            return Task.FromException<TResponse>(new RpcException(_status));
        }

        public Task<Metadata> GetResponseHeadersAsync()
        {
            return Task.FromException<Metadata>(new RpcException(_status));
        }

        public Status GetStatus()
        {
            return _status;
        }

        public Metadata GetTrailers()
        {
            throw new InvalidOperationException("Can't get the call trailers because the call has not completed successfully.");
        }

        public void StartClientStreaming()
        {
            throw new NotSupportedException();
        }

        public void StartDuplexStreaming()
        {
            throw new NotSupportedException();
        }

        public void StartServerStreaming(TRequest request)
        {
            throw new NotSupportedException();
        }

        public void StartUnary(TRequest request)
        {
            throw new NotSupportedException();
        }

        public Task WriteClientStreamAsync<TState>(Func<GrpcCall<TRequest, TResponse>, Stream, CallOptions, TState, ValueTask> writeFunc, TState state)
        {
            return Task.FromException(new RpcException(_status));
        }

        private sealed class StatusClientStreamWriter : IClientStreamWriter<TRequest>
        {
            private readonly Status _status;

            public WriteOptions? WriteOptions { get; set; }

            public StatusClientStreamWriter(Status status)
            {
                _status = status;
            }

            public Task CompleteAsync()
            {
                return Task.FromException(new RpcException(_status));
            }

            public Task WriteAsync(TRequest message)
            {
                return Task.FromException(new RpcException(_status));
            }
        }

        private sealed class StatusStreamReader : IAsyncStreamReader<TResponse>
        {
            private readonly Status _status;

            public TResponse Current { get; set; } = default!;

            public StatusStreamReader(Status status)
            {
                _status = status;
            }

            public Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                return Task.FromException<bool>(new RpcException(_status));
            }
        }
    }
}
