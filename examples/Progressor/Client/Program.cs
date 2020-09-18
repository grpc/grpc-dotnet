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
using System.Threading.Tasks;
using Client.ResponseProgress;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Progress;

namespace Client
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:5001");
            var client = new Progressor.ProgressorClient(channel);

            var progress = new Progress<int>(i => Console.WriteLine($"Progress: {i}%"));

            var result = await ServerStreamingCallExample(client, progress);

            Console.WriteLine("Preparing results...");
            await Task.Delay(TimeSpan.FromSeconds(2));

            foreach (var item in result.Items)
            {
                Console.WriteLine(item);
            }

            Console.WriteLine("Shutting down");
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static ResponseProgress<HistoryResult, int> ServerStreamingCallExample(Progressor.ProgressorClient client, IProgress<int> progress)
        {
            var call = client.RunHistory(new Empty());

            return GrpcProgress.Create(call.ResponseStream, progress);
        }
    }
}
