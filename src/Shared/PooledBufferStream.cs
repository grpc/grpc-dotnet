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
/// A stream backed by a growable buffer rented from <see cref="ArrayPool{T}"/>. It supports the same operations
/// as a <see cref="MemoryStream"/> because compression providers are free to read and seek their output stream.
/// Written data is available until <see cref="Release"/> is called. The instance can be written to again afterwards.
/// </summary>
internal sealed class PooledBufferStream : Stream
{
    // Largest array length supported by the runtime. Array.MaxLength isn't available on all targets.
    private const int MaxBufferSize = 0x7FFFFFC7;

    private byte[]? _buffer;
    private int _position;
    private int _length;

    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            if (value < 0 || value > MaxBufferSize)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _position = (int)value;
        }
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
            _length = 0;
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return ReadCore(new Span<byte>(buffer, offset, count));
    }

    public override int ReadByte()
    {
        return _position < _length ? _buffer![_position++] : -1;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<int>(cancellationToken);
        }

        try
        {
            return Task.FromResult(Read(buffer, offset, count));
        }
        catch (Exception ex)
        {
            return Task.FromException<int>(ex);
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteCore(new ReadOnlySpan<byte>(buffer, offset, count));
    }

    public override void WriteByte(byte value)
    {
        PrepareWrite(1);
        _buffer![_position] = value;
        AdvanceWrite(1);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        try
        {
            Write(buffer, offset, count);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

#if !NETSTANDARD2_0 && !NET462
    public override int Read(Span<byte> buffer)
    {
        return ReadCore(buffer);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<int>(Task.FromCanceled<int>(cancellationToken));
        }

        return new ValueTask<int>(ReadCore(buffer.Span));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteCore(buffer);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask(Task.FromCanceled(cancellationToken));
        }

        try
        {
            WriteCore(buffer.Span);
            return default;
        }
        catch (Exception ex)
        {
            return new ValueTask(Task.FromException(ex));
        }
    }
#endif

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return cancellationToken.IsCancellationRequested
            ? Task.FromCanceled(cancellationToken)
            : Task.CompletedTask;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        var position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (position < 0)
        {
            throw new IOException("An attempt was made to move the position before the beginning of the stream.");
        }

        Position = position;
        return position;
    }

    public override void SetLength(long value)
    {
        if (value < 0 || value > MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var length = (int)value;
        if (length > _length)
        {
            EnsureCapacity(length);
            ClearGap(length);
        }

        _length = length;
        _position = Math.Min(_position, _length);
    }

    protected override void Dispose(bool disposing)
    {
        // Ignore dispose from wrapping compression stream.
        // Written data must be available after the compression stream is disposed. Use Release to return the buffer.
    }

    private int ReadCore(Span<byte> buffer)
    {
        var read = Math.Min(buffer.Length, _length - _position);
        if (read <= 0)
        {
            return 0;
        }

        _buffer.AsSpan(_position, read).CopyTo(buffer);
        _position += read;
        return read;
    }

    private void WriteCore(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length > 0)
        {
            PrepareWrite(buffer.Length);
            buffer.CopyTo(_buffer.AsSpan(_position));
            AdvanceWrite(buffer.Length);
        }
    }

    private void PrepareWrite(int count)
    {
        var required = (long)_position + count;
        if (required > MaxBufferSize)
        {
            throw new InvalidOperationException("Buffer can't be larger than the maximum array length.");
        }

        EnsureCapacity((int)required);

        // The position can be past the end of the written data after seeking.
        if (_position > _length)
        {
            ClearGap(_position);
        }
    }

    private void AdvanceWrite(int count)
    {
        _position += count;
        _length = Math.Max(_length, _position);
    }

    // Rented buffers contain old data so bytes that were never written must be zeroed.
    private void ClearGap(int end)
    {
        _buffer.AsSpan(_length, end - _length).Clear();
    }

    private void EnsureCapacity(int required)
    {
        var capacity = _buffer?.Length ?? 0;
        if (required > capacity)
        {
            Resize((int)Math.Max(required, Math.Min(capacity * 2L, MaxBufferSize)));
        }
    }

    private void Resize(int capacity)
    {
        var newBuffer = ArrayPool<byte>.Shared.Rent(capacity);

        var oldBuffer = _buffer;
        if (oldBuffer != null)
        {
            Buffer.BlockCopy(oldBuffer, 0, newBuffer, 0, _length);
            ArrayPool<byte>.Shared.Return(oldBuffer);
        }

        _buffer = newBuffer;
    }
}
