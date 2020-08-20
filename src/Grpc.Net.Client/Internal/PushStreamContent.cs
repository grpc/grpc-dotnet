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
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Internal
{
    internal class PushStreamContent<TRequest, TResponse> : HttpContent
        where TRequest : class
        where TResponse : class
    {
        private readonly HttpContentClientStreamWriter<TRequest, TResponse> _streamWriter;

        public PushStreamContent(HttpContentClientStreamWriter<TRequest, TResponse> streamWriter, MediaTypeHeaderValue mediaType)
        {
            Headers.ContentType = mediaType;
            _streamWriter = streamWriter;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            // Immediately flush request stream to send headers
            // https://github.com/dotnet/corefx/issues/39586#issuecomment-516210081
            await stream.FlushAsync().ConfigureAwait(false);

            // Pass request stream to writer
            _streamWriter.WriteStreamTcs.TrySetResult(stream);

            // Wait for the writer to report it is complete
            await _streamWriter.CompleteTcs.Task.ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            // We can't know the length of the content being pushed to the output stream.
            length = -1;
            return false;
        }

        // Hacky. ReadAsStreamAsync does not complete until SerializeToStreamAsync finishes
        internal Task PushComplete => ReadAsStreamAsync();
    }
}