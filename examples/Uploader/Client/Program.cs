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

using Google.Protobuf;
using Grpc.Net.Client;
using Upload;

namespace Client
{
    public class Program
    {
        private const int ChunkSize = 1024 * 32; // 32 KB

        static async Task Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Uploader.UploaderClient(channel);

            Console.WriteLine("Starting call");
            var call = client.UploadFile();

            Console.WriteLine("Sending file metadata");
            await call.RequestStream.WriteAsync(new UploadFileRequest
            {
                Metadata = new FileMetadata
                {
                    FileName = "pancakes.jpg"
                }
            });

            var buffer = new byte[ChunkSize];
            await using var readStream = File.OpenRead("pancakes.jpg");

            while (true)
            {
                var count = await readStream.ReadAsync(buffer);
                if (count == 0)
                {
                    break;
                }

                Console.WriteLine("Sending file data chunk of length " + count);
                await call.RequestStream.WriteAsync(new UploadFileRequest
                {
                    Data = UnsafeByteOperations.UnsafeWrap(buffer.AsMemory(0, count))
                });
            }

            Console.WriteLine("Complete request");
            await call.RequestStream.CompleteAsync();

            var response = await call;
            Console.WriteLine("Upload id: " + response.Id);

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
