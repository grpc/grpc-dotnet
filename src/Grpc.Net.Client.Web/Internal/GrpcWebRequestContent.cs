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

using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Web.Internal
{
    internal class GrpcWebRequestContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly GrpcWebMode _mode;

        public GrpcWebRequestContent(HttpContent inner, GrpcWebMode mode)
        {
            _inner = inner;
            _mode = mode;
            foreach (var header in inner.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Headers.ContentType = (mode == GrpcWebMode.GrpcWebText)
                ? GrpcWebProtocolConstants.GrpcWebTextHeader
                : GrpcWebProtocolConstants.GrpcWebHeader;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _mode == GrpcWebMode.GrpcWebText
                ? SerializeTextToStreamAsync(stream)
                : _inner.CopyToAsync(stream);

        private async Task SerializeTextToStreamAsync(Stream stream)
        {
            using var base64RequestStream = new Base64RequestStream(stream);
            await _inner.CopyToAsync(base64RequestStream).ConfigureAwait(false);

            // Any remaining content needs to be written when SerializeToStreamAsync finishes.
            // We want to avoid unnecessary flush calls so a custom method is used to write
            // ramining content rather than calling FlushAsync.
            await base64RequestStream.WriteRemainderAsync(CancellationToken.None).ConfigureAwait(false);
        }

        protected override bool TryComputeLength(out long length)
        {
            // ContentLength is calculated using the inner content's TryComputeLength.
            var contentLength = _inner.Headers.ContentLength;
            if (contentLength != null)
            {
                length = _mode == GrpcWebMode.GrpcWebText
                    ? ((4 * contentLength.GetValueOrDefault() / 3) + 3) & ~3
                    : contentLength.GetValueOrDefault();
                return true;
            }

            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
