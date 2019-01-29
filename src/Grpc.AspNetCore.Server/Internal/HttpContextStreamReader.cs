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
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextStreamReader<TRequest> : IAsyncStreamReader<TRequest>
    {
        private readonly HttpContext _httpContext;
        private readonly Func<byte[], TRequest> _deserializer;

        public HttpContextStreamReader(HttpContext context, Func<byte[], TRequest> deserializer)
        {
            _httpContext = context;
            _deserializer = deserializer;
        }

        public TRequest Current { get; private set; }

        public void Dispose() { }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            var requestPayload = await StreamUtils.ReadMessageAsync(_httpContext.Request.Body);

            if (requestPayload == null)
            {
                Current = default(TRequest);
                return false;
            }

            Current = _deserializer(requestPayload);
            return true;
        }
    }
}
