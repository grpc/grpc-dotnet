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
using System.Threading;
using System.Threading.Tasks;
using Count;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace GRPCServer
{
    public class CounterService : Counter.CounterBase, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IncrementingCounter _counter;

        public CounterService(IncrementingCounter counter, ILoggerFactory loggerFactory)
        {
            _counter = counter;
            _logger = loggerFactory.CreateLogger<CounterService>();
        }

        public override Task<CounterReply> IncrementCount(Empty request, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            _logger.LogInformation($"Connection id: {httpContext.Connection.Id}");

            _logger.LogInformation("Incrementing count by 1");
            _counter.Increment(1);
            return Task.FromResult(new CounterReply { Count = _counter.Count });
        }

        public override async Task<CounterReply> AccumulateCount(IAsyncStreamReader<CounterRequest> requestStream, ServerCallContext context)
        {
            var httpContext = context.GetHttpContext();
            _logger.LogInformation($"Connection id: {httpContext.Connection.Id}");

            while (await requestStream.MoveNext(CancellationToken.None))
            {
                _logger.LogInformation($"Incrementing count by {requestStream.Current.Count}");

                _counter.Increment(requestStream.Current.Count);
            }

            return new CounterReply { Count = _counter.Count };
        }

        public void Dispose()
        {
            _logger.LogInformation("Cleaning up");
        }
    }
}
