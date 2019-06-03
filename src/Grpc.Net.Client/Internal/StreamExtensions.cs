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
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client
{
    internal static partial class StreamExtensions
    {
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static Task<TResponse?> ReadSingleMessageAsync<TResponse>(this Stream responseStream, ILogger logger, Func<byte[], TResponse> deserializer, CancellationToken cancellationToken)
            where TResponse : class
        {
            return responseStream.ReadMessageCoreAsync(logger, deserializer, cancellationToken, true, true);
        }

        public static Task<TResponse?> ReadStreamedMessageAsync<TResponse>(this Stream responseStream, ILogger logger, Func<byte[], TResponse> deserializer, CancellationToken cancellationToken)
            where TResponse : class
        {
            return responseStream.ReadMessageCoreAsync(logger, deserializer, cancellationToken, true, false);
        }

        private static async Task<TResponse?> ReadMessageCoreAsync<TResponse>(this Stream responseStream, ILogger logger, Func<byte[], TResponse> deserializer, CancellationToken cancellationToken, bool canBeEmpty, bool singleMessage)
            where TResponse : class
        {
            try
            {
                Log.ReadingMessage(logger);
                cancellationToken.ThrowIfCancellationRequested();

                // Read the header first
                // - 4 bytes for the content length
                // - 1 byte flag for compression
                var header = new byte[HeaderSize];

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
                        Log.NoMessageReturned(logger);
                        return default;
                    }

                    throw new InvalidDataException("Unexpected end of content while reading the message header.");
                }

                var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
                if (length > int.MaxValue)
                {
                    throw new InvalidDataException("Message too large.");
                }

                // Read message content until content length is reached
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

                Log.DeserializingMessage(logger, messageData.Length, typeof(TResponse));

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

                Log.ReceivedMessage(logger);
                return message;
            }
            catch (Exception ex)
            {
                Log.ErrorReadingMessage(logger, ex);
                throw;
            }
        }

        public static async Task WriteMessage<TMessage>(this Stream stream, ILogger logger, TMessage message, Func<TMessage, byte[]> serializer, CancellationToken cancellationToken)
        {
            try
            {
                Log.SendingMessage(logger);

                // Serialize message first. Need to know size to prefix the length in the header
                var data = serializer(message);

                Log.SerializedMessage(logger, typeof(TMessage), data.Length);

                await WriteHeaderAsync(stream, data.Length, false, cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);

                Log.MessageSent(logger);
            }
            catch (Exception ex)
            {
                Log.ErrorSendingMessage(logger, ex);
                throw;
            }
        }

        private static Task WriteHeaderAsync(Stream stream, int length, bool compress, CancellationToken cancellationToken)
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
