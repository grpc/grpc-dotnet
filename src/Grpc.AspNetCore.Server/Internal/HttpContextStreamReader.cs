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
        private readonly Func<byte[], TRequest> _deserializer;

        public HttpContextStreamReader(HttpContextServerCallContext serverCallContext, Func<byte[], TRequest> deserializer)
        {
            _serverCallContext = serverCallContext;
            _deserializer = deserializer;
        }

        // IAsyncStreamReader<T> should declare Current as nullable
        // Suppress warning when overriding interface definition
#pragma warning disable CS8612 // Nullability of reference types in type doesn't match implicitly implemented member.
        public TRequest? Current { get; private set; }
#pragma warning restore CS8612 // Nullability of reference types in type doesn't match implicitly implemented member.

        public void Dispose() { }

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            async Task<bool> MoveNextAsync(ValueTask<byte[]?> readStreamTask)
            {
                return ProcessPayload(await readStreamTask);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<bool>(cancellationToken);
            }

            var readStreamTask = _serverCallContext.HttpContext.Request.BodyReader.ReadStreamMessageAsync(_serverCallContext, cancellationToken);
            if (!readStreamTask.IsCompletedSuccessfully)
            {
                return MoveNextAsync(readStreamTask);
            }

            return ProcessPayload(readStreamTask.Result) ? True : False;
        }

        private bool ProcessPayload(byte[]? requestPayload)
        {
            // Stream is complete
            if (requestPayload == null)
            {
                Current = default;
                return false;
            }

            Current = _deserializer(requestPayload);
            return true;
        }
    }
}
