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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.NetCore.HttpClient
{
    internal static class StreamExtensions
    {
        public static Task<TResponse> ReadSingleMessageAsync<TResponse>(this Stream responseStream, Func<byte[], TResponse> deserializer, CancellationToken cancellationToken)
        {
            return responseStream.ReadMessageCoreAsync(deserializer, cancellationToken, true, true);
        }

        public static Task<TResponse> ReadStreamedMessageAsync<TResponse>(this Stream responseStream, Func<byte[], TResponse> deserializer, CancellationToken cancellationToken)
        {
            return responseStream.ReadMessageCoreAsync(deserializer, cancellationToken, true, false);
        }

        private static async Task<TResponse> ReadMessageCoreAsync<TResponse>(this Stream responseStream, Func<byte[], TResponse> deserializer, CancellationToken cancellationToken, bool canBeEmpty, bool singleMessage)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var header = new byte[5];

            int read;
            var received = 0;
            while ((read = await responseStream.ReadAsync(header, received, header.Length - received, cancellationToken).ConfigureAwait(false)) > 0)
            {
                received += read;

                if (received == header.Length)
                {
                    break;
                }
            }

            if (received < header.Length)
            {
                if (received == 0 && canBeEmpty)
                {
                    return default;
                }

                throw new InvalidDataException("Unexpected end of content while reading the message header.");
            }

            var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
            if (length > int.MaxValue)
            {
                throw new InvalidDataException("Message too large.");
            }

            byte[] messageData;
            if (length > 0)
            {
                received = 0;
                messageData = new byte[length];
                while ((read = await responseStream.ReadAsync(messageData, received, messageData.Length - received, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += read;

                    if (received == messageData.Length)
                    {
                        break;
                    }
                }
            }
            else
            {
                messageData = Array.Empty<byte>();
            }

            cancellationToken.ThrowIfCancellationRequested();

            var message = deserializer(messageData);

            if (singleMessage)
            {
                // Check that there is no additional content in the stream for a single message
                // There is no ReadByteAsync on stream. Reuse header array with ReadAsync, we don't need it anymore
                if (await responseStream.ReadAsync(header, 0, 1).ConfigureAwait(false) > 0)
                {
                    throw new InvalidDataException("Unexpected data after finished reading message.");
                }
            }

            return message;
        }

    }
}
