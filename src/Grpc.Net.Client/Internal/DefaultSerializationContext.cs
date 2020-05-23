﻿#region Copyright notice and license

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
    internal sealed class DefaultSerializationContext : SerializationContext
    {
        private byte[]? _array;
        private InternalState _state;

        public int? PayloadLength { get; set; }
        private ArrayBufferWriter<byte>? _bufferWriter;

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
            if (_bufferWriter != null)
            {
                // Reuse existing buffer writer
                _bufferWriter.Clear();
            }
            _array = null;
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
                    if (_bufferWriter != null)
                    {
                        payload = _bufferWriter.WrittenMemory;
                        return true;
                    }
                    break;
            }

            payload = default;
            return false;
        }

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
                    _state = InternalState.IncompleteBufferWriter;
                    return ResolveBufferWriter();
                case InternalState.IncompleteBufferWriter:
                    return ResolveBufferWriter();
                default:
                    ThrowInvalidState(_state);
                    return default!;
            }
        }

        private IBufferWriter<byte> ResolveBufferWriter()
        {
            return _bufferWriter ??= PayloadLength.HasValue ? new ArrayBufferWriter<byte>(PayloadLength.Value) : new ArrayBufferWriter<byte>();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidState(InternalState state)
            => throw new InvalidOperationException(state.ToString());

        public override void Complete()
        {
            switch (_state)
            {
                case InternalState.IncompleteBufferWriter:
                    _state = InternalState.CompleteBufferWriter;
                    break;
                default:
                    ThrowInvalidState(_state);
                    break;
            }
        }
    }
}
