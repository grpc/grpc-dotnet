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

namespace Grpc.AspNetCore.Server.Internal.Web
{
    internal static class GrpcWebProtocolHelpers
    {
        // This uses C# compiler's ability to refer to static data directly. For more information see https://vcsjones.dev/2019/02/01/csharp-readonly-span-bytes-static
        private static ReadOnlySpan<byte> CrLf => new[] { (byte)'\r', (byte)'\n' };
        private static ReadOnlySpan<byte> ColonSpace => new[] { (byte)':', (byte)' ' };
        
        private static readonly byte Trailers = 0x80;

        private static void WriteTrailerHeader(PipeWriter output, byte type, uint length)
        {
            var buffer = output.GetSpan(5);

            buffer[0] = type;
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Slice(1), length);

            output.Advance(5);
        }

        public static async Task WriteTrailers(IHeaderDictionary trailers, PipeWriter output)
        {
            // Flush so the last message is written as its own base64 segment
            await output.FlushAsync();

            var size = CalculateHeaderSize(trailers);
            WriteTrailerHeader(output, Trailers, (uint)size);
            WriteTrailersContent(trailers, output);

            await output.FlushAsync();
        }

        private static int CalculateHeaderSize(IHeaderDictionary trailers)
        {
            var total = 0;
            foreach (var kv in trailers)
            {
                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        total += kv.Key.Length + value.Length + 4;
                    }
                }
            }

            return total;
        }

        private static void WriteTrailersContent(IHeaderDictionary trailers, PipeWriter output)
        {
            foreach (var kv in trailers)
            {
                foreach (var value in kv.Value)
                {
                    if (value != null)
                    {
                        output.Write(CrLf);

                        var buffer = output.GetSpan(kv.Key.Length);
                        output.Advance(Encoding.ASCII.GetBytes(kv.Key, buffer));

                        output.Write(ColonSpace);

                        buffer = output.GetSpan(value.Length);
                        output.Advance(Encoding.ASCII.GetBytes(value, buffer));
                    }
                }
            }
        }
    }
}
