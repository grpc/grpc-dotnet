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
using System.Diagnostics;

namespace Grpc.Net.Client.Web.Internal;

internal sealed class Base64ResponseStream : Stream
{
    private readonly Stream _inner;

    private byte[]? _minimumBuffer;
    private int _remainder;
    private byte _remainderByte0;
    private byte _remainderByte1;

    public Base64ResponseStream(Stream inner)
    {
        _inner = inner;
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

        // Handle zero byte reads.
        if (data.Length == 0)
        {
            var read = await StreamHelpers.ReadAsync(_inner, data, cancellationToken).ConfigureAwait(false);
            Debug.Assert(read == 0);
            return 0;
        }

        // There is enough remaining data to fill passed in data
        if (data.Length <= _remainder)
        {
            return CopyRemainderToData(data);
        }

        var underlyingReadData = data;
        var copyFromMinimumBuffer = false;
        if (data.Length < 6)
        {
            // If the requested data is very small, increase it to 6.
            // 4 bytes for base64, and 2 bytes for potential remaining content.
            if (_minimumBuffer == null)
            {
                _minimumBuffer = new byte[4 + 2];
            }

            underlyingReadData = _minimumBuffer;
            copyFromMinimumBuffer = true;
        }
        underlyingReadData = SetRemainder(underlyingReadData);

        // We want to read base64 data in multiples of 4
        underlyingReadData = underlyingReadData.Slice(0, (underlyingReadData.Length / 4) * 4);

        var availableReadData = underlyingReadData;

        var totalRead = 0;
        // Minimum valid base64 length is 4. Read until we have at least that much content
        do
        {
            var read = await StreamHelpers.ReadAsync(_inner, availableReadData, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                if (_remainder > 0)
                {
                    return ReturnData(data, copyFromMinimumBuffer, bytesWritten: 0);
                }
                else if (totalRead == 0)
                {
                    return 0;
                }

                throw new InvalidOperationException("Invalid base64 data.");
            }

            availableReadData = availableReadData.Slice(read);
            totalRead += read;

            // The underlying stream may not have a complete 4 byte segment yet
            // so read again until we have the right data length.
        } while (totalRead % 4 != 0);

        var base64Data = underlyingReadData.Slice(0, totalRead);
        int bytesWritten = DecodeBase64DataFragments(base64Data.Span);

        return ReturnData(data, copyFromMinimumBuffer, bytesWritten);
    }

    internal static int DecodeBase64DataFragments(Span<byte> base64Data)
    {
        var dataLength = 0;
        var remainingBase64Data = base64Data;

        int paddingIndex;
        while (remainingBase64Data.Length > 4 && (paddingIndex = GetPaddingIndex(remainingBase64Data)) != -1)
        {
            var base64Fragment = remainingBase64Data.Slice(0, paddingIndex + 1);
            int bytesWritten = DecodeAndShift(base64Data, dataLength, base64Fragment);

            dataLength += bytesWritten;
            remainingBase64Data = remainingBase64Data.Slice(paddingIndex + 1);
        }

        if (remainingBase64Data.Length > 0)
        {
            dataLength += DecodeAndShift(base64Data, dataLength, remainingBase64Data);
        }

        return dataLength;
    }

    private static int DecodeAndShift(Span<byte> base64Data, int dataLength, Span<byte> base64Fragment)
    {
        EnsureSuccess(Base64.DecodeFromUtf8InPlace(base64Fragment, out var bytesWritten));

        if (dataLength > 0)
        {
            // Shift decoded data. This is required because decoded data is shorter than
            // original base64 content, so must be shifted to avoid gaps in memory
            base64Fragment.Slice(0, bytesWritten).CopyTo(base64Data.Slice(dataLength));
        }

        return bytesWritten;
    }

    private static void EnsureSuccess(OperationStatus status)
    {
        if (status != OperationStatus.Done)
        {
            throw new InvalidOperationException("Error decoding base64 content: " + status);
        }
    }

    private static int GetPaddingIndex(Span<byte> data)
    {
        // Check at the end of every 4 character base64 segement for padding
        for (var i = 3; i < data.Length; i += 4)
        {
            if (data[i] == (byte)'=')
            {
                return i;
            }
        }

        return -1;
    }

    private int ReturnData(Memory<byte> data, bool copyFromMinimumBuffer, int bytesWritten)
    {
        var resolvedRead = bytesWritten + _remainder;

        if (copyFromMinimumBuffer)
        {
            _minimumBuffer.AsMemory(0, data.Length).CopyTo(data);

            PreserveRemainder(_minimumBuffer.AsSpan(data.Length, Math.Max(resolvedRead - data.Length, 0)));
        }
        else
        {
            _remainder = 0;
        }

        return Math.Min(resolvedRead, data.Length);
    }

    private int CopyRemainderToData(Memory<byte> data)
    {
        data.Span[0] = _remainderByte0;
        if (_remainder >= 2)
        {
            if (data.Length > 1)
            {
                data.Span[1] = _remainderByte1;
                _remainder = 0;
            }
            else
            {
                _remainderByte0 = _remainderByte1;
                _remainder = 1;
            }
        }
        else
        {
            _remainder = 0;
        }

        return data.Length;
    }

    private Memory<byte> SetRemainder(Memory<byte> data)
    {
        var span = data.Span;

        if (_remainder >= 1)
        {
            span[0] = _remainderByte0;

            if (_remainder >= 2)
            {
                span[1] = _remainderByte1;
            }
        }

        return data.Slice(_remainder);
    }

    private void PreserveRemainder(Span<byte> remainder)
    {
        Debug.Assert(remainder.Length <= 2);

        if (remainder.Length >= 1)
        {
            _remainderByte0 = remainder[0];

            if (remainder.Length >= 2)
            {
                _remainderByte1 = remainder[1];
            }
        }

        _remainder = remainder.Length;
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
