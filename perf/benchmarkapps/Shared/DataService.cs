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
using System.Linq;
using System.Threading.Tasks;
using Data;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

class DataService : Data.DataService.DataServiceBase
{
    public override async Task DuplexStream(IAsyncStreamReader<DataMessage> requestStream, IServerStreamWriter<DataMessage> responseStream, ServerCallContext context)
    {
        var messageSize = int.Parse(context.RequestHeaders.Single(h => h.Key == "data-size").Value);
        var messageData = ByteString.CopyFrom(new byte[messageSize]);

        var readTask = Task.Run(async () =>
        {
            await foreach (var message in requestStream.ReadAllAsync())
            {
                // Nom nom nom
            }
        });

        // Write outgoing messages until canceled
        while (context.CancellationToken.IsCancellationRequested)
        {
            await responseStream.WriteAsync(new DataMessage
            {
                Data = messageData,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        await readTask;
    }
}