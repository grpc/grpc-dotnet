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
using System.Linq;
using System.Threading.Tasks;
using Grpc.Shared.TestAssets;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace InteropTestsGrpcWebClient.Infrastructure
{
    public class InteropTestInvoker
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly Dictionary<string, string[]> _testNames;
        private readonly ILogger<InteropTestInvoker> _logger;

        public InteropTestInvoker(ILoggerFactory loggerFactory, Dictionary<string, string[]> testNames)
        {
            _loggerFactory = loggerFactory;
            _testNames = testNames;
            _logger = loggerFactory.CreateLogger<InteropTestInvoker>();
        }

        [JSInvokable(nameof(GetTestNames))]
        public string[] GetTestNames(string mode)
        {
            return _testNames
                .Where(kvp => kvp.Value.Contains(mode))
                .Select(kvp => kvp.Key)
                .ToArray();
        }

        [JSInvokable(nameof(RunTestAsync))]
        public async Task<string> RunTestAsync(string serverHost, int serverPort, string grpcWebMode, string testCase)
        {
            var clientOptions = new ClientOptions
            {
                TestCase = testCase,
                ServerHost = serverHost,
                ServerPort = serverPort,
                UseTls = false,
                GrpcWebMode = grpcWebMode
            };
            var client = new InteropClient(clientOptions, _loggerFactory);

            try
            {
                await client.Run();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, ex.Message);
                return ex.ToString();
            }

            return "Success";
        }
    }
}
