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
using Count;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Server.Services
{
    public class CounterService : Counter.CounterBase
    {
        public override async Task StartCounter(Empty request, IServerStreamWriter<CounterResponse> responseStream, ServerCallContext context)
        {
            var count = 0;

            // Attempt to run until canceled by the client
            // Blazor WA is unable to cancel a call that has started - https://github.com/mono/mono/issues/18717
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await responseStream.WriteAsync(new CounterResponse
                {
                    Count = ++count
                });

                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }
    }
}
