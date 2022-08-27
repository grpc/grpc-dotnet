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
using Upload;

namespace Server
{
    public class UploaderService : Uploader.UploaderBase
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _config;

        public UploaderService(ILoggerFactory loggerFactory, IConfiguration config)
        {
            _logger = loggerFactory.CreateLogger<UploaderService>();
            _config = config;
        }

        public override async Task<UploadFileResponse> UploadFile(IAsyncStreamReader<UploadFileRequest> requestStream, ServerCallContext context)
        {
            var uploadId = Path.GetRandomFileName();
            var uploadPath = Path.Combine(_config["StoredFilesPath"]!, uploadId);
            Directory.CreateDirectory(uploadPath);

            await using var writeStream = File.Create(Path.Combine(uploadPath, "data.bin"));

            await foreach (var message in requestStream.ReadAllAsync())
            {
                if (message.Metadata != null)
                {
                    await File.WriteAllTextAsync(Path.Combine(uploadPath, "metadata.json"), message.Metadata.ToString());
                }
                if (message.Data != null)
                {
                    await writeStream.WriteAsync(message.Data.Memory);
                }
            }

            return new UploadFileResponse { Id = uploadId };
        }
    }
}
