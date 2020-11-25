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

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextStreamReader<TRequest> : IAsyncStreamReader<TRequest> where TRequest : class
    {
        private static readonly Task<bool> True = Task.FromResult(true);
        private static readonly Task<bool> False = Task.FromResult(false);

        private readonly HttpContextServerCallContext _serverCallContext;
        private readonly Func<DeserializationContext, TRequest> _deserializer;
        private bool _completed;

        public HttpContextStreamReader(HttpContextServerCallContext serverCallContext, Func<DeserializationContext, TRequest> deserializer)
        {
            _serverCallContext = serverCallContext;
            _deserializer = deserializer;
        }

        // IAsyncStreamReader<T> should declare Current as nullable
        // Suppress warning when overriding interface definition
#pragma warning disable CS8613, CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member.
        public TRequest? Current { get; private set; }
#pragma warning restore CS8613, CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member.

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

            return ProcessPayload(request.Result) ? True : False;
        }

        private bool ProcessPayload(TRequest? request)
        {
            // Stream is complete
            if (request == null)
            {
                Current = null;
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
