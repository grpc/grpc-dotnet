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
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Race;

namespace Server
{
    public class RacerService : Racer.RacerBase
    {
        public override async Task ReadySetGo(IAsyncStreamReader<RaceMessage> requestStream, IServerStreamWriter<RaceMessage> responseStream, ServerCallContext context)
        {
            var raceDuration = TimeSpan.Parse(context.RequestHeaders.GetValue("race-duration")!, CultureInfo.InvariantCulture);

            // Read incoming messages in a background task
            RaceMessage? lastMessageReceived = null;
            var readTask = Task.Run(async () =>
            {
                await foreach (var message in requestStream.ReadAllAsync())
                {
                    lastMessageReceived = message;
                }
            });

            // Write outgoing messages until timer is complete
            var sw = Stopwatch.StartNew();
            var sent = 0;
            while (sw.Elapsed < raceDuration)
            {
                await responseStream.WriteAsync(new RaceMessage { Count = ++sent });
            }

            await readTask;
        }
    }
}
