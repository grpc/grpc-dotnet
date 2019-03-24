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
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace Grpc.NetCore.HttpClient
{
    internal static class PipeWriterExtensions
    {
        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static Task WriteMessageCoreAsync(this PipeWriter pipeWriter, byte[] messageData, bool flush)
        {
            WriteHeader(pipeWriter, messageData.Length);
            pipeWriter.Write(messageData);

            if (flush)
            {
                var valueTask = pipeWriter.FlushAsync();

                if (valueTask.IsCompletedSuccessfully)
                {
                    // We do this to reset the underlying value task (which happens in GetResult())
                    valueTask.GetAwaiter().GetResult();
                    return Task.CompletedTask;
                }

                return valueTask.AsTask();
            }

            return Task.CompletedTask;
        }

        private static void WriteHeader(PipeWriter pipeWriter, int length)
        {
            var headerData = pipeWriter.GetSpan(HeaderSize);
            // Messages are currently always uncompressed
            headerData[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(headerData.Slice(1), (uint)length);

            pipeWriter.Advance(HeaderSize);
        }
    }
}
