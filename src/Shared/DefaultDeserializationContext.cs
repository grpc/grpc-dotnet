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
using System.Diagnostics;
using Grpc.Core;

namespace Grpc.Shared
{
    internal sealed class DefaultDeserializationContext : DeserializationContext
    {
        private ReadOnlySequence<byte>? _payload;

        public void SetPayload(in ReadOnlySequence<byte>? payload)
        {
            _payload = payload;
        }

        public override byte[] PayloadAsNewBuffer()
        {
            Debug.Assert(_payload != null, "Payload must be set.");

            // The array returned by PayloadAsNewBuffer must be the exact message size.
            // There is no opportunity here to return a pooled array.
            return _payload.GetValueOrDefault().ToArray();
        }

        public override ReadOnlySequence<byte> PayloadAsReadOnlySequence()
        {
            Debug.Assert(_payload != null, "Payload must be set.");
            return _payload.GetValueOrDefault();
        }

        public override int PayloadLength => _payload.HasValue ? (int)_payload.GetValueOrDefault().Length : 0;
    }
}
