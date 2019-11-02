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
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Progress;

namespace Server
{
    public class ProgressorService : Progressor.ProgressorBase
    {
        private readonly ILogger _logger;

        public ProgressorService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ProgressorService>();
        }

        public override async Task RunHistory(Empty request, IServerStreamWriter<HistoryResponse> responseStream, ServerCallContext context)
        {
            var monarches = await File.ReadAllLinesAsync("Monarchs-of-England.txt");

            var processedMonarches = new List<string>();
            for (int i = 0; i < monarches.Length; i++)
            {
                // Simulate complex work
                await Task.Delay(TimeSpan.FromSeconds(0.2));

                var monarch = monarches[i];

                // Add monarch to final results
                _logger.LogInformation("Adding {Monarch}", monarch);
                processedMonarches.Add(monarch);

                // Calculate and send progress
                var progress = (i + 1) / (double)monarches.Length;
                await responseStream.WriteAsync(new HistoryResponse
                {
                    Progress = Convert.ToInt32(progress * 100)
                });
            }

            _logger.LogInformation("History complete. Returning {Count} monarchs.", processedMonarches.Count);

            // Send final result
            var historyResult = new HistoryResult();
            historyResult.Items.AddRange(processedMonarches);
            await responseStream.WriteAsync(new HistoryResponse
            {
                Result = historyResult
            });
        }
    }
}
