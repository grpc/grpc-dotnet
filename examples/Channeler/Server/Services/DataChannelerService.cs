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

using System.Threading.Channels;
using DataChannel;
using Grpc.Core;

namespace Server
{
    public class DataChannelerService : DataChanneler.DataChannelerBase
    {
        private readonly ILogger _logger;

        public DataChannelerService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<DataChannelerService>();
        }

        public override async Task<DataResult> UploadData(
            IAsyncStreamReader<DataRequest> requestStream, ServerCallContext context)
        {
            var channel = Channel.CreateBounded<DataRequest>(new BoundedChannelOptions(capacity: 5)
            {
                SingleReader = false,
                SingleWriter = true
            });

            var readTask = Task.Run(async () =>
            {
                await foreach (var message in requestStream.ReadAllAsync())
                {
                    await channel.Writer.WriteAsync(message);
                }

                channel.Writer.Complete();
            });

            // Process incoming messages on three threads.
            var bytesProcessedByThread = await Task.WhenAll(
                ProcessMessagesAsync(channel.Reader, _logger),
                ProcessMessagesAsync(channel.Reader, _logger),
                ProcessMessagesAsync(channel.Reader, _logger));

            await readTask;

            return new DataResult { BytesProcessed = bytesProcessedByThread.Sum() };

            static async Task<int> ProcessMessagesAsync(ChannelReader<DataRequest> reader, ILogger logger)
            {
                var total = 0;
                await foreach (var message in reader.ReadAllAsync())
                {
                    total += message.Value.Length;
                }
                return total;
            }
        }

        public override async Task DownloadResults(DataRequest request,
            IServerStreamWriter<DataResult> responseStream, ServerCallContext context)
        {
            var channel = Channel.CreateBounded<DataResult>(new BoundedChannelOptions(capacity: 5)
            {
                SingleReader = true,
                SingleWriter = false
            });

            var consumerTask = Task.Run(async () =>
            {
                // Consume messages from channel and write to response stream.
                await foreach (var message in channel.Reader.ReadAllAsync())
                {
                    await responseStream.WriteAsync(message);
                }
            });

            var dataChunks = request.Value.Chunk(size: 10);

            // Write messages to channel from multiple threads.
            await Task.WhenAll(dataChunks.Select(
                async c =>
                {
                    var message = new DataResult { BytesProcessed = c.Length };
                    await channel.Writer.WriteAsync(message);
                }));

            // Complete writing and wait for consumer to complete.
            channel.Writer.Complete();
            await consumerTask;
        }
    }
}
