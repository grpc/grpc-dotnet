#region Copyright notice and license

// Copyright 2015 gRPC authors.
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
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf;
using NUnit.Framework;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    internal static class MessageHelpers
    {
        public const int MessageDelimiterSize = 4;

        public static T AssertReadMessage<T>(byte[] messageData) where T : IMessage, new()
        {
            Assert.AreEqual(0, messageData[0]);

            var messageLength = DecodeMessageLength(messageData.AsSpan(1, 4));

            int expectedLength = MessageDelimiterSize + 1 + messageLength;

            Assert.AreEqual(expectedLength, messageData.Length);

            var message = new T();
            message.MergeFrom(messageData.AsSpan(5).ToArray());

            return message;
        }

        public static async Task<T> AssertReadMessageAsync<T>(Stream stream) where T : IMessage, new()
        {
            var messageData = await ReadMessageAsync(stream);
            if (messageData == null)
            {
                return default;
            }

            var message = new T();
            message.MergeFrom(messageData);

            return message;
        }

        public static async Task<byte[]> ReadMessageAsync(Stream stream)
        {
            // read Compressed-Flag and Message-Length
            // as described in https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md
            var delimiterBuffer = new byte[1 + MessageDelimiterSize];
            if (!await ReadExactlyBytesOrNothing(stream, delimiterBuffer, 0, delimiterBuffer.Length))
            {
                return null;
            }

            var compressionFlag = delimiterBuffer[0];
            var messageLength = DecodeMessageLength(new ReadOnlySpan<byte>(delimiterBuffer, 1, MessageDelimiterSize));

            if (compressionFlag != 0)
            {
                // TODO(jtattermusch): support compressed messages
                throw new IOException("Compressed messages are not yet supported.");
            }

            var msgBuffer = new byte[messageLength];
            if (!await ReadExactlyBytesOrNothing(stream, msgBuffer, 0, msgBuffer.Length))
            {
                throw new IOException("Unexpected end of stream.");
            }
            return msgBuffer;
        }

        private static async Task<bool> ReadExactlyBytesOrNothing(Stream stream, byte[] buffer, int offset, int count)
        {
            bool noBytesRead = true;
            while (count > 0)
            {
                int bytesRead = await stream.ReadAsync(buffer, offset, count);
                if (bytesRead == 0)
                {
                    if (noBytesRead)
                    {
                        return false;
                    }
                    throw new IOException("Unexpected end of stream.");
                }
                noBytesRead = false;
                offset += bytesRead;
                count -= bytesRead;
            }
            return true;
        }

        public static async Task WriteMessageAsync<T>(Stream stream, T message) where T : IMessage
        {
            var messageData = message.ToByteArray();

            await WriteMessageAsync(stream, messageData, 0, messageData.Length);
        }

        public static async Task WriteMessageAsync(Stream stream, byte[] buffer, int offset, int count, bool flush = false)
        {
            var delimiterBuffer = new byte[1 + MessageDelimiterSize];
            delimiterBuffer[0] = 0; // = non-compressed
            EncodeMessageLength(count, new Span<byte>(delimiterBuffer, 1, MessageDelimiterSize));
            await stream.WriteAsync(delimiterBuffer, 0, delimiterBuffer.Length);

            await stream.WriteAsync(buffer, offset, count);

            if (flush)
            {
                await stream.FlushAsync();
            }
        }

        public static int DecodeMessageLength(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to decode message length.");
            }

            var result = 0UL;
            for (var i = 0; i < MessageDelimiterSize; i++)
            {
                // msg length stored in big endian
                result = (result << 8) + buffer[i];
            }

            if (result > int.MaxValue)
            {
                throw new IOException("Message too large: " + result);
            }
            return (int)result;
        }

        public static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            if (destination.Length < MessageDelimiterSize)
            {
                throw new ArgumentException("Buffer too small to encode message length.");
            }

            var unsignedValue = (ulong)messageLength;
            for (var i = MessageDelimiterSize - 1; i >= 0; i--)
            {
                // msg length stored in big endian
                destination[i] = (byte)(unsignedValue & 0xff);
                unsignedValue >>= 8;
            }
        }
    }
}
