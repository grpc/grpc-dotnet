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
/// Reads and decodes base64 encoded bytes from the inner reader.
/// </summary>
internal sealed class Base64PipeReader : PipeReader
{
    private readonly PipeReader _inner;
    private ReadOnlySequence<byte> _currentDecodedBuffer;
    private ReadOnlySequence<byte> _currentInnerBuffer;

    // Keep track of how much of the inner result has been read in its own field.
    // Can't use inner buffer length because an inner buffer could be set but not
    // read if it has less than 4 bytes (minimum decode size).
    private long _currentInnerBufferRead;

    public Base64PipeReader(PipeReader inner)
    {
        _inner = inner;
    }

    public override void AdvanceTo(SequencePosition consumed)
    {
        var consumedPosition = ResolvePosition(consumed);

        UpdateCurrentBuffers(consumed, consumedPosition);

        _inner.AdvanceTo(consumedPosition);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        var consumedPosition = ResolvePosition(consumed);

        // If the decoded buffer has no data then this is a cancelled result with some data.
        // Report that we've examined to the end.
        var examinedPosition = _currentDecodedBuffer.Length == 0
            ? _currentInnerBuffer.End
            : ResolvePosition(examined);

        UpdateCurrentBuffers(consumed, consumedPosition);

        _inner.AdvanceTo(consumedPosition, examinedPosition);
    }

    private void UpdateCurrentBuffers(SequencePosition consumed, SequencePosition consumedPosition)
    {
        var lengthBefore = _currentInnerBuffer.Length;

        _currentDecodedBuffer = _currentDecodedBuffer.Slice(consumed);
        _currentInnerBuffer = _currentInnerBuffer.Slice(consumedPosition);

        // Substract difference in the inner buffer from how much has been decoded.
        _currentInnerBufferRead -= lengthBefore - _currentInnerBuffer.Length;
    }

    private SequencePosition ResolvePosition(SequencePosition base64Position)
    {
        // This isn't ideal but it is the standard way to get the length between SequencePositions
        var length = _currentDecodedBuffer.Slice(0, base64Position).Length;

        // IMPORTANT - this logic assumes that SequencePosition will be multiples of 3.
        // GetMaxEncodedToUtf8Length will round up to next multiple of 3, so more data
        // could be advanced internally than expected if not called using a mulitple of 3.
        //
        // Because ReadAsync always returns data in multiples of 3, and gRPC only uses
        // ReadResult.Buffer.End to advance, the position should always be a multiple
        // of 3. The only time it won't be is at the end of a message, in which case
        // rounding up is the correct thing to do to consume padding.
        var endContentPosition = _currentInnerBuffer.GetPosition((length / 3) * 4);

        if (length % 3 == 0)
        {
            return endContentPosition;
        }
        else
        {
            var endPaddingPosition = _currentInnerBuffer.GetPosition(Base64.GetMaxEncodedToUtf8Length((int)length));

            // We should be at the end of the message. Round up to a multiple of 4, but double check that
            // skipped content is base64 padding.
            var paddingSequence = _currentInnerBuffer.Slice(endContentPosition, endPaddingPosition);
            if (paddingSequence.PositionOf((byte)'=') == null)
            {
                throw new InvalidOperationException("AdvanceTo called with an unexpected value. Must advance in multiples of 3 unless at the end of a gRPC message.");
            }

            return endPaddingPosition;
        }
    }

    public override void CancelPendingRead()
    {
        _inner.CancelPendingRead();
    }

    public override void Complete(Exception? exception = null)
    {
        _inner.Complete(exception);

        _currentInnerBuffer = ReadOnlySequence<byte>.Empty;
        _currentDecodedBuffer = ReadOnlySequence<byte>.Empty;
    }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        // ReadAsync needs to handle some common situations:
        // 1. Base64 requires are least 4 bytes to decode content. If less than 4 bytes are returned
        //    from the inner reader then repeatedly call the inner reader until 4 bytes are available.
        // 2. It is possible that ReadAsync is called many times without consuming the data. We don't
        //    want to decode the same base64 content over and over. ReadAsync only decodes new content
        //    and appends it to a sequence.

        var innerResult = await _inner.ReadAsync(cancellationToken);
        if (innerResult.Buffer.IsEmpty)
        {
            _currentDecodedBuffer = innerResult.Buffer;
            _currentInnerBuffer = innerResult.Buffer;
            return innerResult;
        }

        // Minimum valid base64 length is 4. Read until we have at least that much content
        while (innerResult.Buffer.Length - _currentInnerBufferRead < 4)
        {
            if (innerResult.IsCompleted)
            {
                // If the reader completes with less than 4 bytes then the base64 isn't valid..
                throw new InvalidOperationException("Unexpected end of data when reading base64 content.");
            }

            if (innerResult.IsCanceled)
            {
                // Cancelled before we have enough data to decode. Return a cancelled result with no data.
                _currentDecodedBuffer = ReadOnlySequence<byte>.Empty;
                _currentInnerBuffer = innerResult.Buffer;
                return new ReadResult(
                    ReadOnlySequence<byte>.Empty,
                    innerResult.IsCanceled,
                    innerResult.IsCompleted);
            }

            // Attempt to get more data
            _inner.AdvanceTo(innerResult.Buffer.Start, innerResult.Buffer.End);
            innerResult = await _inner.ReadAsync(cancellationToken);
        }

        // Limit result to complete base64 segments (multiples of 4)
        var newResultLength = innerResult.Buffer.Length - _currentInnerBufferRead;
        var newResultValidLength = (newResultLength / 4) * 4;

        var buffer = innerResult.Buffer.Slice(_currentInnerBufferRead, newResultValidLength);

        // The content can contain multiple fragments of base64 content
        // Check for padding, and limit returned data to one fragment at a time
        var paddingIndex = PositionOf(buffer, (byte)'=');
        if (paddingIndex != null)
        {
            buffer = buffer.Slice(0, ((paddingIndex.Value / 4) + 1) * 4);
        }

        // Copy the buffer data to a new array.
        // Need a copy that we own because it will be decoded in place.
        var decodedBuffer = buffer.ToArray();

        var status = Base64.DecodeFromUtf8InPlace(decodedBuffer, out var bytesWritten);
        if (status == OperationStatus.Done || status == OperationStatus.NeedMoreData)
        {
            _currentInnerBuffer = innerResult.Buffer.Slice(0, _currentInnerBufferRead + decodedBuffer.Length);

            _currentInnerBufferRead = _currentInnerBuffer.Length;

            // Update decoded buffer. If there have been multiple reads with the same content then
            // newly decoded content will be appended to the sequence.
            if (_currentDecodedBuffer.IsEmpty)
            {
                // Avoid creating segments for single segment sequence.
                _currentDecodedBuffer = new ReadOnlySequence<byte>(decodedBuffer, 0, bytesWritten);
            }
            else if (_currentDecodedBuffer.IsSingleSegment)
            {
                var start = new MemorySegment<byte>(_currentDecodedBuffer.First);

                // Append new content to end.
                var end = start.Append(decodedBuffer.AsMemory(0, bytesWritten));

                _currentDecodedBuffer = new ReadOnlySequence<byte>(start, 0, end, end.Memory.Length);
            }
            else
            {
                var start = (MemorySegment<byte>)_currentDecodedBuffer.Start.GetObject()!;
                var end = (MemorySegment<byte>)_currentDecodedBuffer.End.GetObject()!;

                // Append new content to end.
                end = end.Append(decodedBuffer.AsMemory(0, bytesWritten));

                _currentDecodedBuffer = new ReadOnlySequence<byte>(start, 0, end, end.Memory.Length);
            }

            return new ReadResult(
                _currentDecodedBuffer,
                innerResult.IsCanceled,
                innerResult.IsCompleted);
        }

        throw new InvalidOperationException("Unexpected status: " + status);
    }

    public override bool TryRead(out ReadResult result)
    {
        throw new NotImplementedException();
    }

    private static int? PositionOf(in ReadOnlySequence<byte> source, byte value)
    {
        if (source.IsSingleSegment)
        {
            int index = source.First.Span.IndexOf(value);
            if (index != -1)
            {
                return index;
            }

            return null;
        }
        else
        {
            return PositionOfMultiSegment(source, value);
        }
    }

    private static int? PositionOfMultiSegment(in ReadOnlySequence<byte> source, byte value)
    {
        var position = source.Start;
        var total = 0;
        while (source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
        {
            int index = memory.Span.IndexOf(value);
            if (index != -1)
            {
                return total + index;
            }
            else if (position.GetObject() == null)
            {
                break;
            }

            total += memory.Length;
        }

        return null;
    }
}
