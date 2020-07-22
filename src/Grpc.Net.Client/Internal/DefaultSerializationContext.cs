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

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Grpc.Core;

namespace Grpc.Net.Client.Internal
{
    internal sealed class DefaultSerializationContext : SerializationContext, IBufferWriter<byte>
    {
        private byte[]? _array;
        private InternalState _state;
        private int _writerPosition;

        public int? PayloadLength { get; set; }

        private enum InternalState : byte
        {
            Initialized,
            CompleteArray,
            IncompleteBufferWriter,
            CompleteBufferWriter,
        }

        public void Reset()
        {
            PayloadLength = null;
            if (IsBufferWriter)
            {
                ArrayPool<byte>.Shared.Return(_array);
            }
            _array = null;
            _writerPosition = 0;
            _state = InternalState.Initialized;
        }

        /// <summary>
        /// Obtains the payload from this operation, and returns a boolean indicating
        /// whether the serialization was complete; the state is reset either way.
        /// </summary>
        public bool TryGetPayload(out ReadOnlyMemory<byte> payload)
        {
            switch (_state)
            {
                case InternalState.CompleteArray:
                    payload = _array;
                    return true;
                case InternalState.CompleteBufferWriter:
                    payload = _array.AsMemory(GrpcProtocolConstants.HeaderSize, PayloadLength.GetValueOrDefault());
                    return true;
            }

            payload = default;
            return false;
        }

        public bool IsBufferWriter => _state == InternalState.IncompleteBufferWriter || _state == InternalState.CompleteBufferWriter;

        public Memory<byte> GetUnderlyingArray() => _array;

        public override void SetPayloadLength(int payloadLength)
        {
            PayloadLength = payloadLength;
        }

        public override void Complete(byte[] payload)
        {
            switch (_state)
            {
                case InternalState.Initialized:
                    _array = payload;
                    _state = InternalState.CompleteArray;
                    break;
                default:
                    ThrowInvalidState(_state);
                    break;
            }
        }

        public override IBufferWriter<byte> GetBufferWriter()
        {
            switch (_state)
            {
                case InternalState.Initialized:
                    Debug.Assert(PayloadLength != null, "SetPayloadLength must have already been called.");

                    _state = InternalState.IncompleteBufferWriter;
                    // Leave room for the header to be written in front of the payload data
                    _array = ArrayPool<byte>.Shared.Rent(GrpcProtocolConstants.HeaderSize + PayloadLength.GetValueOrDefault());
                    return this;
                case InternalState.IncompleteBufferWriter:
                    return this;
                default:
                    ThrowInvalidState(_state);
                    return default!;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidState(InternalState state)
            => throw new InvalidOperationException(state.ToString());

        public override void Complete()
        {
            switch (_state)
            {
                case InternalState.IncompleteBufferWriter:
                    Debug.Assert(_writerPosition == PayloadLength.GetValueOrDefault(), "Must have written to the end of the payload length.");
                    _state = InternalState.CompleteBufferWriter;
                    break;
                default:
                    ThrowInvalidState(_state);
                    break;
            }
        }

        void IBufferWriter<byte>.Advance(int count)
        {
            Debug.Assert(_writerPosition + count <= PayloadLength.GetValueOrDefault(), "Can't advance past the total payload length.");
            _writerPosition += count;
        }

        Memory<byte> IBufferWriter<byte>.GetMemory(int sizeHint)
        {
            // Leave room for the header to be written in front of the payload data
            return _array.AsMemory(GrpcProtocolConstants.HeaderSize + _writerPosition);
        }

        Span<byte> IBufferWriter<byte>.GetSpan(int sizeHint)
        {
            // Leave room for the header to be written in front of the payload data
            return _array.AsSpan(GrpcProtocolConstants.HeaderSize + _writerPosition);
        }
    }
}
