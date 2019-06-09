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
using System.Threading;
using System.Threading.Tasks;
using Count;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sample.Clients
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly Counter.CounterClient _counterClient;
        private readonly Random _random;
        private AsyncClientStreamingCall<CounterRequest, CounterReply>? _clientStreamingCall;

        public Worker(ILogger<Worker> logger, Counter.CounterClient counterClient)
        {
            _logger = logger;
            _counterClient = counterClient;
            _random = new Random();
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting client streaming call at: {time}", DateTimeOffset.Now);
            _clientStreamingCall = _counterClient.AccumulateCount();

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Debug.Assert(_clientStreamingCall != null);

            // Count until the worker exits
            while (!stoppingToken.IsCancellationRequested)
            {
                var count = _random.Next(1, 10);

                _logger.LogInformation("Sending count {count} at: {time}", count, DateTimeOffset.Now);
                await _clientStreamingCall.RequestStream.WriteAsync(new CounterRequest { Count = count });

                await Task.Delay(1000, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(_clientStreamingCall != null);

            // Tell server that the client stream has finished
            _logger.LogInformation("Finishing call at: {time}", DateTimeOffset.Now);
            await _clientStreamingCall.RequestStream.CompleteAsync();

            // Log total
            var response = await _clientStreamingCall;
            _logger.LogInformation("Total count: {count}", response.Count);

            await base.StopAsync(cancellationToken);
        }
    }
}
