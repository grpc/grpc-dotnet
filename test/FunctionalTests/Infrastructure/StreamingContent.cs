﻿#region Copyright notice and license

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

using System.Net;
using System.Net.Http.Headers;
using Grpc.AspNetCore.Server.Internal;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    internal class StreamingContent : HttpContent
    {
        private readonly TaskCompletionSource<Stream> _streamTcs;
        private readonly TaskCompletionSource<object?> _contentTcs;

        public StreamingContent(MediaTypeHeaderValue? mediaType = null)
        {
            _streamTcs = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
            _contentTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

            Headers.ContentType = mediaType ?? new MediaTypeHeaderValue(GrpcProtocolConstants.GrpcContentType);
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            _streamTcs.TrySetResult(stream);
            return _contentTcs.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            // We can't know the length of the content being pushed to the output stream.
            length = -1;
            return false;
        }

        public Task<Stream> GetRequestStreamAsync()
        {
            return _streamTcs.Task;
        }

        public void Complete()
        {
            _contentTcs.TrySetResult(null);
        }
    }
}
