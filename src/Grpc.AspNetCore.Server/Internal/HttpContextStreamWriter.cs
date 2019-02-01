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

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextStreamWriter<TResponse> : IServerStreamWriter<TResponse>
    {
        private readonly HttpContextServerCallContext _context;
        private readonly GrpcServiceOptions _serviceOptions;
        private readonly Func<TResponse, byte[]> _serializer;

        public HttpContextStreamWriter(HttpContextServerCallContext context, GrpcServiceOptions serviceOptions, Func<TResponse, byte[]> serializer)
        {
            _context = context;
            _serviceOptions = serviceOptions;
            _serializer = serializer;
        }

        public WriteOptions WriteOptions
        {
            get => _context.WriteOptions;
            set => _context.WriteOptions = value;
        }

        public Task WriteAsync(TResponse message) => _context.HttpContext.Response.BodyPipe.WriteMessageAsync(message, _serviceOptions, _serializer, WriteOptions);
    }
}
