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
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextStreamWriter<TResponse> : IServerStreamWriter<TResponse>
    {
        private readonly HttpContext _httpContext;
        private readonly Func<TResponse, byte[]> _serializer;

        public HttpContextStreamWriter(HttpContext context, Func<TResponse, byte[]> serializer)
        {
            _httpContext = context;
            _serializer = serializer;
        }

        public WriteOptions WriteOptions { get; set; }

        public Task WriteAsync(TResponse message)
        {
            // TODO: make sure the response is not null
            var responsePayload = _serializer(message);

            // Flush messages unless WriteOptions.Flags has BufferHint set
            var flush = ((WriteOptions?.Flags ?? default(WriteFlags)) & WriteFlags.BufferHint) != WriteFlags.BufferHint;

            return StreamUtils.WriteMessageAsync(_httpContext.Response.Body, responsePayload, 0, responsePayload.Length, flush);
        }
    }
}
