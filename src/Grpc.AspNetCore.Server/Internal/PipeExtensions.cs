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
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class PipeExtensions
    {
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static Task WriteMessageAsync<TResponse>(this PipeWriter pipeWriter, TResponse response, Func<TResponse, byte[]> serializer, WriteOptions writeOptions)
        {
            var responsePayload = serializer(response);

            // Flush messages unless WriteOptions.Flags has BufferHint set
            var flush = ((writeOptions?.Flags ?? default) & WriteFlags.BufferHint) != WriteFlags.BufferHint;

            return pipeWriter.WriteMessageAsync(responsePayload, flush);
        }

        public static Task WriteMessageAsync(this PipeWriter pipeWriter, byte[] messageData, bool flush = false)
        {
            WriteHeader(pipeWriter, messageData.Length);
            pipeWriter.Write(messageData);

            if (flush)
            {
                // Avoid async state machine
                return FlushWriterAsync(pipeWriter);
            }

            return Task.CompletedTask;

            async Task FlushWriterAsync(PipeWriter p)
            {
                await p.FlushAsync();
            }
        }

        private static void WriteHeader(PipeWriter pipeWriter, int length)
        {
            Span<byte> headerData = pipeWriter.GetSpan(HeaderSize);
            // Messages are currently always uncompressed
            headerData[0] = 0;
            EncodeMessageLength(length, headerData.Slice(1));

            pipeWriter.Advance(HeaderSize);
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= MessageDelimiterSize, "Buffer too small to encode message length.");

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }

        private static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
        {
            Debug.Assert(buffer.Length >= MessageDelimiterSize, "Buffer too small to decode message length.");

            var result = BinaryPrimitives.ReadUInt32BigEndian(buffer);

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }

            return (int)result;
        }

        private static bool TryReadHeader(ReadOnlySequence<byte> buffer, out bool compressed, out int messageLength)
        {
            if (buffer.Length < HeaderSize)
            {
                compressed = false;
                messageLength = 0;
                return false;
            }

            if (buffer.First.Length >= HeaderSize)
            {
                var headerData = buffer.First.Span.Slice(0, HeaderSize);

                compressed = ReadCompressedFlag(headerData[0]);
                messageLength = DecodeMessageLength(headerData.Slice(1));
            }
            else
            {
                Span<byte> headerData = stackalloc byte[HeaderSize];
                buffer.Slice(0, HeaderSize).CopyTo(headerData);

                compressed = ReadCompressedFlag(headerData[0]);
                messageLength = DecodeMessageLength(headerData.Slice(1));
            }

            return true;
        }

        private static bool ReadCompressedFlag(byte flag)
        {
            if (flag == 0)
            {
                return false;
            }
            else if (flag == 1)
            {
                return true;
            }
            else
            {
                throw new InvalidDataException("Unexpected compressed flag value in message header.");
            }
        }

        /// <summary>
        /// Read a single message from the pipe reader. Ensure the reader completes without additional data.
        /// </summary>
        /// <param name="input">The request pipe reader.</param>
        /// <returns>Complete message data.</returns>
        public static async ValueTask<byte[]> ReadSingleMessageAsync(this PipeReader input)
        {
            byte[] completeMessageData = null;

            while (true)
            {
                var result = await input.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (result.IsCanceled)
                    {
                        throw new InvalidDataException("Incoming message cancelled.");
                    }

                    if (!buffer.IsEmpty)
                    {
                        if (completeMessageData != null)
                        {
                            throw new InvalidDataException("Additional data after the message received.");
                        }

                        if (TryReadMessage(ref buffer, out var data))
                        {
                            // Store the message data
                            // Need to verify the request completes with no additional data
                            completeMessageData = data;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        if (completeMessageData != null)
                        {
                            // Finished and the complete message has arrived
                            return completeMessageData;
                        }

                        throw new InvalidDataException("Incomplete message.");
                    }
                }
                finally
                {
                    // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }

        /// <summary>
        /// Read a message in a stream from the pipe reader. Additional message data is left in the reader.
        /// </summary>
        /// <param name="input">The request pipe reader.</param>
        /// <returns>Complete message data or null if the stream is complete.</returns>
        public static async ValueTask<byte[]> ReadStreamMessageAsync(this PipeReader input)
        {
            while (true)
            {
                var result = await input.ReadAsync();
                var buffer = result.Buffer;

                try
                {
                    if (result.IsCanceled)
                    {
                        throw new InvalidDataException("Incoming message cancelled.");
                    }

                    if (!buffer.IsEmpty)
                    {
                        if (TryReadMessage(ref buffer, out var data))
                        {
                            return data;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        if (buffer.Length == 0)
                        {
                            // Finished and there is no more data
                            return null;
                        }

                        throw new InvalidDataException("Incomplete message.");
                    }
                }
                finally
                {
                    // The buffer was sliced up to where it was consumed, so we can just advance to the start.
                    // We mark examined as buffer.End so that if we didn't receive a full frame, we'll wait for more data
                    // before yielding the read again.
                    input.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }

        private static bool TryReadMessage(ref ReadOnlySequence<byte> buffer, out byte[] message)
        {
            if (!TryReadHeader(buffer, out var compressed, out var messageLength))
            {
                message = null;
                return false;
            }

            if (compressed)
            {
                // TODO(jtattermusch): support compressed messages
                throw new InvalidDataException("Compressed messages are not yet supported.");
            }

            if (buffer.Length < HeaderSize + messageLength)
            {
                message = null;
                return false;
            }

            // Convert message to byte array
            var messageBuffer = buffer.Slice(HeaderSize, messageLength);
            message = messageBuffer.ToArray();

            // Update buffer to remove message
            buffer = buffer.Slice(HeaderSize + messageLength);

            return true;
        }
    }
}
