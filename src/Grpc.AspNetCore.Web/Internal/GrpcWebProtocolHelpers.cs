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
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Web.Internal
{
    internal static class GrpcWebProtocolHelpers
    {
        private const byte Cr = (byte)'\r';
        private const byte Lf = (byte)'\n';
        private const byte Colon = (byte)':';
        private const byte Space = (byte)' ';

        // Special trailers byte with eigth most significant bit set.
        // Parsers will use this to identify regular messages vs trailers.
        private static readonly byte TrailersSignifier = 0x80;

        private static readonly int HeaderSize = 5;

        public static async Task WriteTrailersAsync(IHeaderDictionary trailers, PipeWriter output)
        {
            // Flush so the last message is written as its own base64 segment
            await output.FlushAsync();

            WriteTrailers(trailers, output);

            await output.FlushAsync();
        }

        internal static void WriteTrailers(IHeaderDictionary trailers, IBufferWriter<byte> output)
        {
            // Precalculate trailer size. Required for trailers header metadata
            var contentSize = CalculateHeaderSize(trailers);

            var totalSize = contentSize + HeaderSize;
            var buffer = output.GetSpan(totalSize);

            WriteTrailersHeader(buffer, contentSize);
            WriteTrailersContent(buffer.Slice(HeaderSize), trailers);

            output.Advance(totalSize);
        }

        private static void WriteTrailersHeader(Span<byte> buffer, int length)
        {
            buffer[0] = TrailersSignifier;
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(1), (uint)length);
        }

        private static int CalculateHeaderSize(IHeaderDictionary trailers)
        {
            // Calculate the header size and validate keys and values only contain value characters.
            var total = 0;
            foreach (var kv in trailers)
            {
                var name = kv.Key;

                var invalidNameIndex = HttpCharacters.IndexOfInvalidTokenChar(name);
                if (invalidNameIndex != -1)
                {
                    ThrowInvalidHeaderCharacter(name[invalidNameIndex]);
                }

                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        var invalidFieldIndex = HttpCharacters.IndexOfInvalidFieldValueChar(value);
                        if (invalidFieldIndex != -1)
                        {
                            ThrowInvalidHeaderCharacter(value[invalidFieldIndex]);
                        }

                        // Key + value + 2 (': ') + 2 (\r\n)
                        total += name.Length + value.Length + 4;
                    }
                }
            }

            return total;
        }

        private static void ThrowInvalidHeaderCharacter(char ch)
        {
            throw new InvalidOperationException($"Invalid non-ASCII or control character in header: 0x{((ushort)ch):X4}");
        }

        private static void WriteTrailersContent(Span<byte> buffer, IHeaderDictionary trailers)
        {
            var currentBuffer = buffer;

            foreach (var kv in trailers)
            {
                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        // Get lower-case ASCII bytes for the key.
                        // gRPC-Web protocol says that names should be lower-case and grpc-web JS client
                        // will check for 'grpc-status' and 'grpc-message' in trailers with lower-case key.
                        // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-WEB.md#protocol-differences-vs-grpc-over-http2
                        for (var i = 0; i < kv.Key.Length; i++)
                        {
                            char c = kv.Key[i];
                            currentBuffer[i] = (byte)((uint)(c - 'A') <= ('Z' - 'A') ? c | 0x20 : c);
                        }

                        var position = kv.Key.Length;

                        currentBuffer[position++] = Colon;
                        currentBuffer[position++] = Space;

                        position += Encoding.ASCII.GetBytes(value, currentBuffer.Slice(position));

                        currentBuffer[position++] = Cr;
                        currentBuffer[position++] = Lf;

                        currentBuffer = currentBuffer.Slice(position);
                    }
                }
            }
        }
    }
}
