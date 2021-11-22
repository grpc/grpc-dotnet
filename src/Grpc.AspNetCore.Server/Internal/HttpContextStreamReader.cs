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
using Grpc.Shared;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextStreamReader<TRequest> : IAsyncStreamReader<TRequest> where TRequest : class
    {
        private readonly HttpContextServerCallContext _serverCallContext;
        private readonly Func<DeserializationContext, TRequest> _deserializer;
        private bool _completed;

        public HttpContextStreamReader(HttpContextServerCallContext serverCallContext, Func<DeserializationContext, TRequest> deserializer)
        {
            _serverCallContext = serverCallContext;
            _deserializer = deserializer;
        }

        public TRequest Current { get; private set; } = default!;

        public void Dispose() { }

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            async Task<bool> MoveNextAsync(ValueTask<TRequest?> readStreamTask)
            {
                return ProcessPayload(await readStreamTask);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }

            if (_completed || _serverCallContext.CancellationToken.IsCancellationRequested)
            {
                return Task.FromException<bool>(new InvalidOperationException("Can't read messages after the request is complete."));
            }

            var request = _serverCallContext.HttpContext.Request.BodyReader.ReadStreamMessageAsync(_serverCallContext, _deserializer, cancellationToken);
            if (!request.IsCompletedSuccessfully)
            {
                return MoveNextAsync(request);
            }

            return ProcessPayload(request.Result)
                ? CommonGrpcProtocolHelpers.TrueTask
                : CommonGrpcProtocolHelpers.FalseTask;
        }

        private bool ProcessPayload(TRequest? request)
        {
            // Stream is complete
            if (request == null)
            {
                Current = null!;
                return false;
            }

            Current = request;
            return true;
        }

        public void Complete()
        {
            _completed = true;
        }
    }
}
