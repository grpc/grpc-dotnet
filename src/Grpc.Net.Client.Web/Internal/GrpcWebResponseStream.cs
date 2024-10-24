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

using System.Buffers.Binary;
using System.Data;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;

namespace Grpc.Net.Client.Web.Internal;

/// <summary>
/// This stream keeps track of messages in the response, and inspects the message header to see if it is
/// for trailers. When the trailers message is encountered then they are parsed as HTTP/1.1 trailers and
/// added to the HttpResponseMessage.TrailingHeaders.
/// </summary>
internal sealed class GrpcWebResponseStream : Stream
{
    private const int HeaderLength = 5;

    private readonly Stream _inner;
    private readonly HttpHeaders _responseTrailers;
    private byte[]? _headerBuffer;

    // Internal for testing
    internal ResponseState _state;
    internal int _contentRemaining;

    public GrpcWebResponseStream(Stream inner, HttpHeaders responseTrailers)
    {
        _inner = inner;
        _responseTrailers = responseTrailers;
    }

#if NETSTANDARD2_0
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
#else
    public override async ValueTask<int> ReadAsync(Memory<byte> data, CancellationToken cancellationToken = default)
#endif
    {
#if NETSTANDARD2_0
        var data = buffer.AsMemory(offset, count);
#endif
        var headerBuffer = Memory<byte>.Empty;

        if (data.Length == 0)
        {
            // Handle zero byte reads.
            var read = await StreamHelpers.ReadAsync(_inner, data, cancellationToken).ConfigureAwait(false);
            Debug.Assert(read == 0);
            return 0;
        }

        switch (_state)
        {
            case ResponseState.Ready:
                {
                    // Read the header first
                    // - 1 byte flag for compression
                    // - 4 bytes for the content length
                    _contentRemaining = HeaderLength;
                    _state = ResponseState.Header;
                    goto case ResponseState.Header;
                }
            case ResponseState.Header:
                {
                    Debug.Assert(_contentRemaining > 0);

                    headerBuffer = data.Length >= _contentRemaining ? data.Slice(0, _contentRemaining) : data;
                    var success = await TryReadDataAsync(_inner, headerBuffer, cancellationToken).ConfigureAwait(false);
                    if (!success)
                    {
                        return 0;
                    }

                    // On first read of header data, check first byte to see if this is a trailer.
                    if (_contentRemaining == HeaderLength)
                    {
                        var compressed = headerBuffer.Span[0];
                        var isTrailer = IsBitSet(compressed, pos: 7);
                        if (isTrailer)
                        {
                            _state = ResponseState.Trailer;
                            goto case ResponseState.Trailer;
                        }
                    }

                    var read = headerBuffer.Length;

                    // The buffer was less than header length either because this is the first read and the passed in buffer is small,
                    // or it is an additonal read to finish getting header data.
                    if (headerBuffer.Length < HeaderLength)
                    {
                        _headerBuffer ??= new byte[HeaderLength];
                        headerBuffer.CopyTo(_headerBuffer.AsMemory(HeaderLength - _contentRemaining));

                        _contentRemaining -= headerBuffer.Length;
                        if (_contentRemaining > 0)
                        {
                            return read;
                        }

                        headerBuffer = _headerBuffer;
                    }

                    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.Span.Slice(1));

                    _contentRemaining = length;
                    // If there is no content then state is reset to ready.
                    _state = _contentRemaining > 0 ? ResponseState.Content : ResponseState.Ready;
                    return read;
                }
            case ResponseState.Content:
                {
                    if (data.Length >= _contentRemaining)
                    {
                        data = data.Slice(0, _contentRemaining);
                    }

                    var read = await StreamHelpers.ReadAsync(_inner, data, cancellationToken).ConfigureAwait(false);
                    _contentRemaining -= read;
                    if (_contentRemaining == 0)
                    {
                        _state = ResponseState.Ready;
                    }

                    return read;
                }
            case ResponseState.Trailer:
                {
                    Debug.Assert(headerBuffer.Length > 0);

                    // The trailer needs to be completely read before returning 0 to the caller.
                    // Ensure buffer is large enough to contain the trailer header.
                    if (headerBuffer.Length < HeaderLength)
                    {
                        var newBuffer = new byte[HeaderLength];
                        headerBuffer.CopyTo(newBuffer);
                        var success = await TryReadDataAsync(_inner, newBuffer.AsMemory(headerBuffer.Length), cancellationToken).ConfigureAwait(false);
                        if (!success)
                        {
                            return 0;
                        }
                        headerBuffer = newBuffer;
                    }
                    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(headerBuffer.Span.Slice(1));

                    await ReadTrailersAsync(length, data, cancellationToken).ConfigureAwait(false);
                    return 0;
                }
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
        var count = await StreamHelpers.ReadAsync(_inner, data, cancellationToken).ConfigureAwait(false);
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

            var lineEndIndex = remainingContent.IndexOf("\r\n"u8);
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

                var name = GetString(Trim(line.Slice(0, headerDelimiterIndex)));
                var value = GetString(Trim(line.Slice(headerDelimiterIndex + 1)));

                _responseTrailers.Add(name, value);
            }
        }
    }

    private static string GetString(ReadOnlySpan<byte> span)
    {
#if NETSTANDARD2_0
        return Encoding.ASCII.GetString(span.ToArray());
#else
        return Encoding.ASCII.GetString(span);
#endif
    }

    internal static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> span)
    {
        var startIndex = -1;
        for (var i = 0; i < span.Length; i++)
        {
            if (!char.IsWhiteSpace((char)span[i]))
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
            if (!char.IsWhiteSpace((char)span[i]))
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
        while ((read = await StreamHelpers.ReadAsync(responseStream, buffer.Slice(received, buffer.Length - received), cancellationToken).ConfigureAwait(false)) > 0)
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

    internal enum ResponseState
    {
        Ready,
        Header,
        Content,
        Trailer,
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
