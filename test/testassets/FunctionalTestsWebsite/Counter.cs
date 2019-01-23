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

using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Count;
using Microsoft.Extensions.Logging;
using FunctionalTestsWebsite.Infrastructure;

namespace FunctionalTestsWebsite
{
    public class CounterService : Counter.CounterBase
    {
        private readonly ILogger _logger;
        private readonly IncrementingCounter _counter;
        private readonly Signaler _signaler;

        public CounterService(IncrementingCounter counter, ILoggerFactory loggerFactory, Signaler signaler)
        {
            _counter = counter;
            _signaler = signaler;
            _logger = loggerFactory.CreateLogger<CounterService>();
        }

        public override Task<CounterReply> IncrementCount(Empty request, ServerCallContext context)
        {
            _logger.LogInformation("Incrementing count by 1");
            _counter.Increment(1);
            return Task.FromResult(new CounterReply { Count = _counter.Count });
        }

        public override async Task<CounterReply> AccumulateCount(IAsyncStreamReader<CounterRequest> requestStream, ServerCallContext context)
        {
            while (await requestStream.MoveNextAsync())
            {
                _logger.LogInformation($"Incrementing count by {requestStream.Current.Count}");

                _counter.Increment(requestStream.Current.Count);

                // Signal client that message was received
                _signaler.Set();
            }

            // Signal client that exiting
            _signaler.Set();

            return new CounterReply { Count = _counter.Count };
        }
    }
}
