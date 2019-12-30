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

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Web.Internal
{
    /// <summary>
    /// This stream keeps track of messages in the response, and inspects the message header to see if it is
    /// for trailers. When the trailers message is encountered then they are parsed as HTTP/1.1 trailers and
    /// added to the HttpResponseMessage.TrailingHeaders.
    /// </summary>
    internal class GrpcWebResponseStream : Stream
    {
        // This uses C# compiler's ability to refer to static data directly. For more information see https://vcsjones.dev/2019/02/01/csharp-readonly-span-bytes-static
        private static ReadOnlySpan<byte> BytesNewLine => new byte[] { (byte)'\r', (byte)'\n' };

        private readonly Stream _inner;
        private readonly HttpHeaders _responseTrailers;
        private int _contentRemaining;
        private ResponseState _state;

        public GrpcWebResponseStream(Stream inner, HttpHeaders responseTrailers)
        {
            _inner = inner;
            _responseTrailers = responseTrailers;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> data, CancellationToken cancellationToken = default)
        {
            switch (_state)
            {
                case ResponseState.Ready:
                    // Read the header first
                    // - 1 byte flag for compression
                    // - 4 bytes for the content length
                    Memory<byte> headerBuffer;

                    if (data.Length >= 5)
                    {
                        headerBuffer = data.Slice(0, 5);
                    }
                    else
                    {
                        // Should never get here. Client always passes 5 to read the header.
                        throw new InvalidOperationException("Buffer is not large enough for header");
                    }

                    var success = await TryReadDataAsync(_inner, headerBuffer, cancellationToken).ConfigureAwait(false);
                    if (!success)
                    {
                        return 0;
                    }

                    var compressed = headerBuffer.Span[0];
                    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.Span.Slice(1));

                    var isTrailer = IsBitSet(compressed, pos: 7);
                    if (isTrailer)
                    {
                        await ReadTrailersAsync(length, data, cancellationToken).ConfigureAwait(false);
                        return 0;
                    }

                    _contentRemaining = length;
                    // If there is no content then state is still ready
                    _state = _contentRemaining > 0 ? ResponseState.Content : ResponseState.Ready;
                    return 5;
                case ResponseState.Content:
                    if (data.Length >= _contentRemaining)
                    {
                        data = data.Slice(0, _contentRemaining);
                    }

                    var read = await _inner.ReadAsync(data, cancellationToken);
                    _contentRemaining -= read;
                    if (_contentRemaining == 0)
                    {
                        _state = ResponseState.Ready;
                    }

                    return read;
                default:
                    throw new InvalidOperationException("Unexpected state.");
            }
        }

        private async Task ReadTrailersAsync(int trailerLength, Memory<byte> data, CancellationToken cancellationToken)
        {
            if (trailerLength > 0)
            {
                // Read trailers into memory. Attempt to reuse existing buffer, otherwise allocate
                // a new buffer of the trailer size.
                if (trailerLength > data.Length)
                {
                    data = new byte[trailerLength];
                }
                else if (trailerLength < data.Length)
                {
                    data = data.Slice(0, trailerLength);
                }

                var success = await TryReadDataAsync(_inner, data, cancellationToken).ConfigureAwait(false);
                if (!success)
                {
                    throw new InvalidOperationException("Could not read trailing headers.");
                }

                ParseTrailers(data.Span);
            }

            // Final read to ensure there is no additional data. This is important:
            // 1. Double checks there is no unexpected additional data after trailers.
            // 2. The response stream is read to completion. HttpClient may not recognize the
            //    request as completing successfully if the request and response aren't completely
            //    consumed.
            var count = await _inner.ReadAsync(data, cancellationToken);
            if (count > 0)
            {
                throw new InvalidOperationException("Unexpected data after trailers.");
            }

            _state = ResponseState.Complete;
        }

        private void ParseTrailers(ReadOnlySpan<byte> span)
        {
            // Key-value pairs encoded as a HTTP/1 headers block (without the terminating newline),
            // per https://tools.ietf.org/html/rfc7230#section-3.2
            //
            // This parsing logic doesn't support line folding.
            //
            // JavaScript gRPC-Web trailer parsing logic for comparison:
            // https://github.com/grpc/grpc-web/blob/55ebde4719c7ad5e58aaa5205cdbd77a76ea9de3/javascript/net/grpc/web/grpcwebclientreadablestream.js#L292-L309

            var remainingContent = span;
            while (remainingContent.Length > 0)
            {
                ReadOnlySpan<byte> line;

                var lineEndIndex = remainingContent.IndexOf(BytesNewLine);
                if (lineEndIndex == -1)
                {
                    line = remainingContent;
                    remainingContent = ReadOnlySpan<byte>.Empty;
                }
                else
                {
                    line = remainingContent.Slice(0, lineEndIndex);
                    remainingContent = remainingContent.Slice(lineEndIndex + 2);
                }

                if (line.Length > 0)
                {
                    var headerDelimiterIndex = line.IndexOf((byte)':');
                    if (headerDelimiterIndex == -1)
                    {
                        throw new InvalidOperationException("Error parsing badly formatted trailing header.");
                    }

                    var name = Encoding.ASCII.GetString(Trim(line.Slice(0, headerDelimiterIndex)));
                    var value = Encoding.ASCII.GetString(Trim(line.Slice(headerDelimiterIndex + 1)));

                    _responseTrailers.Add(name, value);
                }
            }
        }

        internal static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
        {
            var startIndex = -1;
            for (var i = 0; i < span.Length; i++)
            {
                if (!char.IsWhiteSpace((char) span[i]))
                {
                    startIndex = i;
                    break;
                }
            }

            if (startIndex == -1)
            {
                return ReadOnlySpan<byte>.Empty;
            }

            var endIndex = span.Length - 1;
            for (var i = endIndex; i >= startIndex; i--)
            {
                if (!char.IsWhiteSpace((char) span[i]))
                {
                    endIndex = i;
                    break;
                }
            }

            return span.Slice(startIndex, (endIndex - startIndex) + 1);
        }

        private static bool IsBitSet(byte b, int pos)
        {
            return ((b >> pos) & 1) != 0;
        }

        private static async Task<bool> TryReadDataAsync(Stream responseStream, Memory<byte> buffer, CancellationToken cancellationToken)
        {
            int read;
            var received = 0;
            while ((read = await responseStream.ReadAsync(buffer.Slice(received, buffer.Length - received), cancellationToken).ConfigureAwait(false)) > 0)
            {
                received += read;

                if (received == buffer.Length)
                {
                    return true;
                }
            }

            if (received == 0)
            {
                return false;
            }

            throw new InvalidDataException("Unexpected end of content while reading response stream.");
        }

        private enum ResponseState
        {
            Ready,
            Content,
            Complete
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Stream implementation
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set { _inner.Position = value; }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
