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
using System.IO.Pipelines;

namespace Grpc.AspNetCore.Web.Internal;

/// <summary>
/// Writes bytes as base64 encoded to the inner writer.
/// </summary>
internal sealed class Base64PipeWriter : PipeWriter
{
    private readonly PipeWriter _inner;
    // We have to write original data to buffer. GetSpan/GetMemory isn't guaranteed to return the
    // original data if we call it again on Advance so we can't use it as temporary buffer.
    private byte[]? _buffer;
    private int _remainder;

    // Internal for unit testing
    internal byte _remainderByte0;
    internal byte _remainderByte1;

    public Base64PipeWriter(PipeWriter inner)
    {
        _inner = inner;
    }

    public override void Advance(int bytes)
    {
        if (bytes == 0)
        {
            return;
        }

        var resolvedBytes = bytes + _remainder;
        var newRemainder = resolvedBytes % 3;
        var bytesToProcess = resolvedBytes - newRemainder;

        if (bytesToProcess > 0)
        {
            PreserveRemainder(_buffer.AsSpan(bytesToProcess, newRemainder));

            // When writing base64 content we don't want any padding until the end of the message,
            // at which point FlushAsync is called.
            // Process data in intervals of 3, and save the remainder at the start of a new span.
            var buffer = _inner.GetSpan((bytesToProcess / 3) * 4);
            CoreAdvance(_buffer.AsSpan(0, bytesToProcess), buffer);
        }
        else
        {
            // Don't have at least 3 bytes to write a base64 segment.
            // Bytes are preserved in the remainder for the next Advance.
            PreserveRemainder(_buffer.AsSpan(0, resolvedBytes));
        }
    }

    private void CoreAdvance(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        EnsureSuccess(Base64.EncodeToUtf8(source, destination, out _, out var bytesWritten));

        _inner.Advance(bytesWritten);
    }

    private void EnsureSuccess(OperationStatus status)
    {
        if (status != OperationStatus.Done)
        {
            throw new InvalidOperationException("Error encoding content to base64: " + status);
        }
    }

    private void PreserveRemainder(Span<byte> buffer)
    {
        if (buffer.Length >= 1)
        {
            _remainderByte0 = buffer[0];

            if (buffer.Length >= 2)
            {
                _remainderByte1 = buffer[1];
                _remainder = 2;
            }
            else
            {
                _remainder = 1;
            }
        }
        else
        {
            _remainder = 0;
        }
    }

    public override void CancelPendingFlush()
    {
        _inner.CancelPendingFlush();
    }

    public override void Complete(Exception? exception = null)
    {
        if (exception == null)
        {
            WriteRemainder();
        }

        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        _inner.Complete(exception);
    }

    public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
    {
        // FlushAsync is called at the end of a message or the response. Write the remainder with padding.
        WriteRemainder();

        return _inner.FlushAsync(cancellationToken);
    }

    public override Memory<byte> GetMemory(int sizeHint = 0)
    {
        // Get size plus the current remainder (it is included at the start of the data returned)
        if (_buffer == null || _buffer.Length < sizeHint + _remainder)
        {
            if (_buffer != null)
            {
                ArrayPool<byte>.Shared.Return(_buffer);
            }

            _buffer = ArrayPool<byte>.Shared.Rent(sizeHint + _remainder);
        }

        if (_remainder > 0)
        {
            SetRemainder(_buffer.AsSpan());
            return _buffer.AsMemory(_remainder);
        }

        return _buffer;
    }

    public override Span<byte> GetSpan(int sizeHint = 0)
    {
        return GetMemory(sizeHint).Span;
    }

    private void SetRemainder(Span<byte> span)
    {
        span[0] = _remainderByte0;
        if (_remainder >= 2)
        {
            span[1] = _remainderByte1;
        }
    }

    private void WriteRemainder()
    {
        // Write remaining data. Padding is automatically added.
        if (_remainder > 0)
        {
            var buffer = _inner.GetSpan(4);

            Span<byte> remainder = stackalloc byte[2];
            SetRemainder(remainder);
            CoreAdvance(remainder.Slice(0, _remainder), buffer);

            _remainder = 0;
        }
    }
}
