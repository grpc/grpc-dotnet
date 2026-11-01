#region Copyright notice and license

// Copyright 2026 The gRPC Authors
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

namespace Grpc.Shared;

/// <summary>
/// A write-only stream backed by a growable buffer rented from <see cref="ArrayPool{T}"/>.
/// Written data is available until <see cref="Release"/> is called.
/// </summary>
internal sealed class PooledBufferStream : Stream
{
    // Largest array length supported by the runtime. Array.MaxLength isn't available on all targets.
    private const int MaxBufferSize = 0x7FFFFFC7;

    private byte[]? _buffer;
    private int _position;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _position);

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _position;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns the buffer to the pool and discards written data.
    /// </summary>
    public void Release()
    {
        var buffer = _buffer;
        if (buffer != null)
        {
            _buffer = null;
            _position = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureAvailable(count);
        Buffer.BlockCopy(buffer, offset, _buffer!, _position, count);
        _position += count;
    }

    public override void WriteByte(byte value)
    {
        EnsureAvailable(1);
        _buffer![_position++] = value;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Write(buffer, offset, count);
        return Task.CompletedTask;
    }

#if !NETSTANDARD2_0 && !NET462
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureAvailable(buffer.Length);
        buffer.CopyTo(_buffer.AsSpan(_position));
        _position += buffer.Length;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Write(buffer.Span);
        return default;
    }
#endif

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // Ignore dispose from wrapping compression stream.
        // Written data must be available after the compression stream is disposed. Use Release to return the buffer.
    }

    private void EnsureAvailable(int count)
    {
        var required = (long)_position + count;
        var capacity = _buffer?.Length ?? 0;
        if (required > capacity)
        {
            if (required > MaxBufferSize)
            {
                throw new InvalidOperationException("Buffer can't be larger than the maximum array length.");
            }

            Resize((int)Math.Max(required, Math.Min(capacity * 2L, MaxBufferSize)));
        }
    }

    private void Resize(int capacity)
    {
        var newBuffer = ArrayPool<byte>.Shared.Rent(capacity);

        var oldBuffer = _buffer;
        if (oldBuffer != null)
        {
            Buffer.BlockCopy(oldBuffer, 0, newBuffer, 0, _position);
            ArrayPool<byte>.Shared.Return(oldBuffer);
        }

        _buffer = newBuffer;
    }
}
