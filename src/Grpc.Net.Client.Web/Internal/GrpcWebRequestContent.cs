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
                Headers.Add(header.Key, header.Value);
            }

            Headers.ContentType = (mode == GrpcWebMode.GrpcWebText)
                ? GrpcWebProtocolConstants.GrpcWebTextHeader
                : GrpcWebProtocolConstants.GrpcWebHeader;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            Base64RequestStream? base64RequestStream = null;

            try
            {
                if (_mode == GrpcWebMode.GrpcWebText)
                {
                    base64RequestStream = new Base64RequestStream(stream);
                    stream = base64RequestStream;
                }

                await _inner.CopyToAsync(stream).ConfigureAwait(false);
            }
            finally
            {
                base64RequestStream?.Dispose();
            }
        }

        protected override bool TryComputeLength(out long length)
        {
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
