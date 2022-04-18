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

using Grpc.Core;
using Download;
using Google.Protobuf;

namespace Server
{
    public class DownloaderService : Downloader.DownloaderBase
    {
        private readonly ILogger _logger;
        private const int ChunkSize = 1024 * 32;
        
        public DownloaderService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<DownloaderService>();
        }

        public override async Task DownloadFile(DownloadFileRequest request, IServerStreamWriter<DownloadFileResponse> responseStream, ServerCallContext context)
        {
            var requestParam = request.Id; 
            var filename = requestParam switch
            {
                "4" => "pancakes4.png",
                _ => "pancakes.jpg",
            };

            await responseStream.WriteAsync(new DownloadFileResponse
            {
                Metadata = new FileMetadata { FileName = filename }
            });

            var buffer = new byte[ChunkSize];
            await using var fileStream = File.OpenRead(filename);

            while (true)
            {
                var numBytesRead = await fileStream.ReadAsync(buffer);
                if (numBytesRead == 0)
                {
                    break;
                }

                _logger.LogInformation("Sending data chunk of {numBytesRead} bytes", numBytesRead);
                await responseStream.WriteAsync(new DownloadFileResponse
                {
                    Data = UnsafeByteOperations.UnsafeWrap(buffer.AsMemory(0, numBytesRead))
                }) ;
            }
        }
    }
}
