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

using System.Buffers;
using System.Buffers.Text;

namespace Grpc.Net.Client.Web.Internal;

internal sealed class Base64RequestStream : Stream
{
    private readonly Stream _inner;
    private byte[]? _buffer;
    private int _remainder;

    public Base64RequestStream(Stream inner)
    {
        _inner = inner;
    }

#if NETSTANDARD2_0
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
#else
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
#endif
    {
#if NETSTANDARD2_0
        var data = buffer.AsMemory(offset, count);
#endif

        if (_buffer == null)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(minimumLength: 4096);
        }

        // Data has to be encoded to base64 in increments of 3
        // 1. First handle any remaining data from the last call to WriteAsync
        //    - There may still not be enough data (e.g. 1 byte + 1 byte). Add to remaining data and exit
        //    - Use remaining data to write to buffer
        // 2. Write data to buffer and then write buffer until there is not enough data remaining
        // 3. Save remainder to buffer

        Memory<byte> localBuffer;
        if (_remainder > 0)
        {
            var required = 3 - _remainder;
            if (data.Length < required)
            {
                // There is remainder and the new buffer doesn't have enough content for the
                // remainder to be written as base64
                data.CopyTo(_buffer.AsMemory(_remainder));
                _remainder += data.Length;
                return;
            }

            // Use data to complete remainder and write to buffer
            data.Slice(0, required).CopyTo(_buffer.AsMemory(_remainder));
            EnsureSuccess(Base64.EncodeToUtf8InPlace(_buffer, 3, out var bytesWritten));

            // Trim used data
            data = data.Slice(required);
            localBuffer = _buffer.AsMemory(bytesWritten);
        }
        else
        {
            localBuffer = _buffer;
        }

        while (data.Length >= 3)
        {
            // Final encoded data length could exceed buffer length
            // When this happens the data will be encoded and WriteAsync in a loop
            var encodeLength = Math.Min(data.Length, localBuffer.Length / 4 * 3);

            EnsureSuccess(
                Base64.EncodeToUtf8(data.Span.Slice(0, encodeLength), localBuffer.Span, out var bytesConsumed, out var bytesWritten, isFinalBlock: false),
#if NETSTANDARD2_1 || NETSTANDARD2_0
                OperationStatus.NeedMoreData
#else
                // React to fix https://github.com/dotnet/runtime/pull/281
                encodeLength == bytesConsumed ? OperationStatus.Done : OperationStatus.NeedMoreData
#endif
                );

            var base64Remainder = _buffer.Length - localBuffer.Length;
            await StreamHelpers.WriteAsync(_inner, _buffer, 0, bytesWritten + base64Remainder, cancellationToken).ConfigureAwait(false);

            data = data.Slice(bytesConsumed);
            localBuffer = _buffer;
        }

        // Remainder content will usually be written with other data
        // If there was not enough data to write along with remainder then write it here
        if (localBuffer.Length < _buffer.Length)
        {
            await StreamHelpers.WriteAsync(_inner, _buffer, 0, 4, cancellationToken).ConfigureAwait(false);
        }

        if (data.Length > 0)
        {
            data.CopyTo(_buffer);
        }

        // Remainder can be 0-2 bytes
        _remainder = data.Length;
    }

    private static void EnsureSuccess(OperationStatus status, OperationStatus expectedStatus = OperationStatus.Done)
    {
        if (status != expectedStatus)
        {
            throw new InvalidOperationException($"Error encoding content to base64. Expected status: {expectedStatus}, actual status: {status}.");
        }
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await WriteRemainderAsync(cancellationToken).ConfigureAwait(false);
        await _inner.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task WriteRemainderAsync(CancellationToken cancellationToken)
    {
        if (_remainder > 0)
        {
            EnsureSuccess(Base64.EncodeToUtf8InPlace(_buffer, _remainder, out var bytesWritten));

            await StreamHelpers.WriteAsync(_inner, _buffer!, 0, bytesWritten, cancellationToken).ConfigureAwait(false);
            _remainder = 0;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }
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
        // Used by unit tests
#if NETSTANDARD2_0
        WriteAsync(buffer, 0, count).GetAwaiter().GetResult();
#else
        WriteAsync(buffer.AsMemory(0, count)).AsTask().GetAwaiter().GetResult();
#endif
        FlushAsync().GetAwaiter().GetResult();
    }
    #endregion
}
