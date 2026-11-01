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
        if (_buffer != null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
            _position = 0;
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteCore(new ReadOnlySpan<byte>(buffer, offset, count));
    }

#if !NETSTANDARD2_0 && !NET462
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteCore(buffer);
    }
#endif

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // Ignore dispose from wrapping compression stream.
        // Written data must be available after the compression stream is disposed. Use Release to return the buffer.
    }

    private void WriteCore(ReadOnlySpan<byte> data)
    {
        EnsureAvailable(data.Length);
        data.CopyTo(_buffer.AsSpan(_position));
        _position += data.Length;
    }

    private void EnsureAvailable(int count)
    {
        var capacity = _buffer?.Length ?? 0;
        var required = (long)_position + count;
        if (required <= capacity)
        {
            return;
        }

        if (required > MaxBufferSize)
        {
            throw new InvalidOperationException("Buffer can't be larger than the maximum array length.");
        }

        var newBuffer = ArrayPool<byte>.Shared.Rent((int)Math.Min(Math.Max(required, capacity * 2L), MaxBufferSize));

        if (_buffer != null)
        {
            _buffer.AsSpan(0, _position).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        _buffer = newBuffer;
    }
}
