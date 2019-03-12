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
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using SingletonCount;

namespace FunctionalTestsWebsite.Services
{
    public class SingletonCounterService : Counter.CounterBase, IDisposable
    {
        private readonly ILogger _logger;
        private int _count = 0;

        public SingletonCounterService(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<CounterService>();
        }

        public void Dispose()
        {
            // Set the count value to some arbitrary value. This should never happen in any case.
            _count = int.MinValue;
        }

        public override Task<CounterReply> IncrementCount(Empty request, ServerCallContext context)
        {
            _logger.LogInformation("Incrementing count by 1");
            return Task.FromResult(new CounterReply { Count = ++_count });
        }
    }
}
