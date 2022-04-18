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

using Download;
using Grpc.Core;
using Grpc.Net.Client;

namespace Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:5001");

            var client = new Downloader.DownloaderClient(channel);
           
            var downloadsPath = Path.Combine(Environment.CurrentDirectory, "downloads");
            var downloadId = Path.GetRandomFileName();
            var downloadIdPath = Path.Combine(downloadsPath, downloadId);
            Directory.CreateDirectory(downloadIdPath);

            Console.WriteLine("Starting call");

            using var call = client.DownloadFile(new DownloadFileRequest
            {
                Id = downloadId
            });

            await using var writeStream = File.Create(Path.Combine(downloadIdPath, "data.bin"));
            
            while (await call.ResponseStream.MoveNext())
            {
                var message = call.ResponseStream.Current;
                if (message.Metadata != null)
                {
                    Console.WriteLine("Saving metadata to file"); 
                    string metadata = message.Metadata.ToString();                    
                    await File.WriteAllTextAsync(Path.Combine(downloadIdPath, "metadata.json"), metadata);
                }
                if (message.Data != null)
                {
                    var bytes = message.Data.Memory;
                    Console.WriteLine($"Saving {bytes.Length} bytes to file");
                    await writeStream.WriteAsync(bytes);
                }
            }

            Console.WriteLine("\nFiles were saved in: " + downloadIdPath);
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }
    }
}
