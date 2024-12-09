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

namespace Grpc.AspNetCore.Server.Internal;

// Potentially remove in the future when https://github.com/dotnet/corefx/issues/31804 is implemented
internal sealed class ReadOnlySequenceStream : Stream
{
    private static readonly Task<int> TaskOfZero = Task.FromResult(0);

    private Task<int>? _lastReadTask;
    private readonly ReadOnlySequence<byte> _readOnlySequence;
    private SequencePosition _position;

    public ReadOnlySequenceStream(ReadOnlySequence<byte> readOnlySequence)
    {
        _readOnlySequence = readOnlySequence;
        _position = readOnlySequence.Start;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _readOnlySequence.Length;

    public override long Position
    {
        get => _readOnlySequence.Slice(0, _position).Length;
        set
        {
            _position = _readOnlySequence.GetPosition(value, _readOnlySequence.Start);
        }
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        var remaining = _readOnlySequence.Slice(_position);
        var toCopy = remaining.Slice(0, Math.Min(buffer.Length, remaining.Length));
        _position = toCopy.End;
        toCopy.CopyTo(buffer);
        return (int)toCopy.Length;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytesRead = Read(buffer.AsSpan(offset, count));
        if (bytesRead == 0)
        {
            return TaskOfZero;
        }

        if (_lastReadTask?.Result == bytesRead)
        {
            return _lastReadTask;
        }
        else
        {
            return _lastReadTask = Task.FromResult(bytesRead);
        }
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<int>(Read(buffer.Span));
    }

    public override int ReadByte()
    {
        var remaining = _readOnlySequence.Slice(_position);
        if (remaining.Length > 0)
        {
            var result = remaining.First.Span[0];
            _position = _readOnlySequence.GetPosition(1, _position);
            return result;
        }
        else
        {
            return -1;
        }
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        SequencePosition relativeTo;
        switch (origin)
        {
            case SeekOrigin.Begin:
                relativeTo = _readOnlySequence.Start;
                break;
            case SeekOrigin.Current:
                if (offset >= 0)
                {
                    relativeTo = _position;
                }
                else
                {
                    relativeTo = _readOnlySequence.Start;
                    offset += Position;
                }

                break;
            case SeekOrigin.End:
                if (offset >= 0)
                {
                    relativeTo = _readOnlySequence.End;
                }
                else
                {
                    relativeTo = _readOnlySequence.Start;
                    offset += Position;
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        _position = _readOnlySequence.GetPosition(offset, relativeTo);
        return Position;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void WriteByte(byte value) => throw new NotSupportedException();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
    {
        foreach (var segment in _readOnlySequence)
        {
            await destination.WriteAsync(segment, cancellationToken).ConfigureAwait(false);
        }
    }
}
