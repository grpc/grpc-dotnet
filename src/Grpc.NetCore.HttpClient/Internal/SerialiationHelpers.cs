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
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.NetCore.HttpClient.Internal
{
    internal static class SerializationHelpers
    {
        public static async Task WriteMessage<TMessage>(Stream stream, TMessage message, Func<TMessage, byte[]> serializer, CancellationToken cancellationToken)
        {
            var data = serializer(message);

            await WriteHeaderAsync(stream, data.Length, false, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }

        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static Task WriteHeaderAsync(Stream stream, int length, bool compress, CancellationToken cancellationToken)
        {
            var headerData = new byte[HeaderSize];

            // Compression flag
            headerData[0] = compress ? (byte)1 : (byte)0;

            // Message length
            EncodeMessageLength(length, headerData.AsSpan(1));

            return stream.WriteAsync(headerData, 0, headerData.Length, cancellationToken);
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= MessageDelimiterSize, "Buffer too small to encode message length.");

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }
    }
}
