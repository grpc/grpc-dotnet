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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Streaming;

namespace FunctionalTestsWebsite.Services
{
    public class StreamService : Streaming.StreamService.StreamServiceBase
    {
        public override async Task DuplexData(
            IAsyncStreamReader<DataMessage> requestStream,
            IServerStreamWriter<DataMessage> responseStream,
            ServerCallContext context)
        {
            // Read data into MemoryStream
            var ms = new MemoryStream();
            while (await requestStream.MoveNext(CancellationToken.None))
            {
                ms.Write(requestStream.Current.Data.Span);
                if (requestStream.Current.FinalSegment)
                {
                    break;
                }
            }

            // Write back to client in batches
            var data = ms.ToArray();
            var sent = 0;
            while (sent < data.Length)
            {
                const int BatchSize = 1024 * 64; // 64 KB

                var writeCount = Math.Min(data.Length - sent, BatchSize);
                var finalWrite = sent + writeCount == data.Length;
                await responseStream.WriteAsync(new DataMessage
                {
                    Data = ByteString.CopyFrom(data, sent, writeCount),
                    FinalSegment = finalWrite
                });

                sent += writeCount;
            }
        }

        public override async Task<DataComplete> ClientStreamedData(
            IAsyncStreamReader<DataMessage> requestStream,
            ServerCallContext context)
        {
            var total = 0L;
            while (await requestStream.MoveNext(CancellationToken.None))
            {
                var message = requestStream.Current;
                total += message.Data.Length;

                if (message.ServerDelayMilliseconds > 0)
                {
                    await Task.Delay(message.ServerDelayMilliseconds);
                }
            }

            return new DataComplete
            {
                Size = total
            };
        }
    }
}