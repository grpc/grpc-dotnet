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

using System.Text;
using DataChannel;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;

using var channel = GrpcChannel.ForAddress("https://localhost:5001");
var client = new DataChanneler.DataChannelerClient(channel);

await UploadDataAsync(client);

await DownloadResultsAsync(client);

Console.WriteLine("Shutting down");
Console.WriteLine("Press any key to exit...");
Console.ReadKey();

static async Task UploadDataAsync(DataChanneler.DataChannelerClient client)
{
    var call = client.UploadData();

    var dataChunks = TestData.Chunk(5);
    foreach (var chunk in dataChunks)
    {
        Console.WriteLine($"Uploading chunk: {chunk.Length} bytes");
        await call.RequestStream.WriteAsync(new DataRequest { Value = ByteString.CopyFrom(chunk) });
    }

    await call.RequestStream.CompleteAsync();

    var result = await call;
    Console.WriteLine($"Total upload processed: {result.BytesProcessed} bytes");
}

static async Task DownloadResultsAsync(DataChanneler.DataChannelerClient client)
{
    var call = client.DownloadResults(new DataRequest { Value = ByteString.CopyFrom(TestData) });

    await foreach (var result in call.ResponseStream.ReadAllAsync())
    {
        Console.WriteLine($"Downloaded bytes processed result: {result.BytesProcessed}");
    }
}

public partial class Program
{
    private static readonly byte[] TestData = Encoding.UTF8.GetBytes("The quick brown fox jumped over the lazy dog.");
}
