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

using Microsoft.AspNetCore.Authorization.Infrastructure;
using System;
using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Web.Internal
{
    /// <summary>
    /// Reads and decodes base64 encoded bytes from the inner reader.
    /// </summary>
    internal class Base64PipeReader : PipeReader
    {
        private readonly PipeReader _inner;
        private ReadOnlySequence<byte> _currentInnerBuffer;
        private ReadOnlySequence<byte> _currentDecodedBuffer;
        private byte[]? _rentedBuffer;

        public Base64PipeReader(PipeReader inner)
        {
            _inner = inner;
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
            var consumedPosition = ResolvePosition(consumed);

            _inner.AdvanceTo(consumedPosition);

            ReturnBuffer();
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            var consumedPosition = ResolvePosition(consumed);

            // If the decoded buffer has no data then this is a cancelled result with some data.
            // Report that we've examined to the end.
            var examinedPosition = _currentDecodedBuffer.Length == 0
                ? _currentInnerBuffer.End
                : ResolvePosition(examined);

            _inner.AdvanceTo(consumedPosition, examinedPosition);

            ReturnBuffer();
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

            ReturnBuffer();
        }

        private void ReturnBuffer()
        {
            if (_rentedBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_rentedBuffer);
                _rentedBuffer = null;
            }
        }

        public async override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var innerResult = await _inner.ReadAsync(cancellationToken);
            if (innerResult.Buffer.IsEmpty)
            {
                _currentDecodedBuffer = innerResult.Buffer;
                _currentInnerBuffer = innerResult.Buffer;
                return innerResult;
            }

            // Minimum valid base64 length is 4. Read until we have at least that much content
            while (innerResult.Buffer.Length < 4)
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
            var buffer = innerResult.Buffer.Slice(0, (innerResult.Buffer.Length / 4) * 4);

            // The content can contain multiple fragments of base64 content
            // Check for padding, and limit returned data to one fragment at a time
            var paddingIndex = PositionOf(buffer, (byte)'=');
            if (paddingIndex != null)
            {
                _currentInnerBuffer = buffer.Slice(0, ((paddingIndex.Value / 4) + 1) * 4);
            }
            else
            {
                _currentInnerBuffer = buffer;
            }

            var length = (int)_currentInnerBuffer.Length;
            // Any rented buffer should have been returned
            Debug.Assert(_rentedBuffer == null);
            _rentedBuffer = ArrayPool<byte>.Shared.Rent(length);
            _currentInnerBuffer.CopyTo(_rentedBuffer);

            var validLength = (length / 4) * 4;
            var status = Base64.DecodeFromUtf8InPlace(_rentedBuffer.AsSpan(0, validLength), out var bytesWritten);
            if (status == OperationStatus.Done || status == OperationStatus.NeedMoreData)
            {
                _currentDecodedBuffer = new ReadOnlySequence<byte>(_rentedBuffer, 0, bytesWritten);
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

        private static bool ValidatePadding(in ReadOnlySequence<byte> source)
        {
            if (source.IsSingleSegment)
            {
                return ValidatePaddingCore(source.First.Span);
            }
            else
            {
                return ValidatePaddingMultiSegment(source);
            }
        }

        private static bool ValidatePaddingMultiSegment(in ReadOnlySequence<byte> source)
        {
            var position = source.Start;
            
            while (source.TryGet(ref position, out ReadOnlyMemory<byte> memory))
            {
                if (!ValidatePaddingCore(memory.Span))
                {
                    return false;
                }
                
                if (position.GetObject() == null)
                {
                    break;
                }
            }

            return true;
        }

        private static bool ValidatePaddingCore(ReadOnlySpan<byte> span)
        {
            for (var i = 0; i < span.Length; i++)
            {
                if (span[i] != '=')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
