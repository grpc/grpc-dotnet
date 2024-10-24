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

using System.Net;
using System.Net.Http.Headers;

namespace Grpc.Net.Client.Web.Internal;

internal sealed class GrpcWebResponseContent : HttpContent
{
    private readonly HttpContent _inner;
    private readonly GrpcWebMode _mode;
    private readonly HttpHeaders _responseTrailers;
    private Stream? _innerStream;

    public GrpcWebResponseContent(HttpContent inner, GrpcWebMode mode, HttpHeaders responseTrailers)
    {
        _inner = inner;
        _mode = mode;
        _responseTrailers = responseTrailers;

        foreach (var header in inner.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        Headers.ContentType = GrpcWebProtocolConstants.GrpcHeader;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        // This method will only be called by tests when response content is
        // accessed via ReadAsBytesAsync. The gRPC client will always
        // call ReadAsStreamAsync, which will call CreateContentReadStreamAsync.

        _innerStream = await _inner.ReadAsStreamAsync().ConfigureAwait(false);

        if (_mode == GrpcWebMode.GrpcWebText)
        {
            _innerStream = new Base64ResponseStream(_innerStream);
        }

        _innerStream = new GrpcWebResponseStream(_innerStream, _responseTrailers);

        await _innerStream.CopyToAsync(stream).ConfigureAwait(false);
    }

    protected override async Task<Stream> CreateContentReadStreamAsync()
    {
        var stream = await _inner.ReadAsStreamAsync().ConfigureAwait(false);

        if (_mode == GrpcWebMode.GrpcWebText)
        {
            stream = new Base64ResponseStream(stream);
        }

        return new GrpcWebResponseStream(stream, _responseTrailers);
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
            // This is important. Disposing original response content will cancel the gRPC call.
            _inner.Dispose();
            _innerStream?.Dispose();
        }

        base.Dispose(disposing);
    }
}
