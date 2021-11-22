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

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Tests.FunctionalTests.Helpers
{
    internal class GrpcTestContext<TStartup> : IDisposable where TStartup : class
    {
        private readonly ExecutionContext _executionContext;
        private readonly Stopwatch _stopwatch;
        private readonly GrpcTestFixture<TStartup> _fixture;

        public GrpcTestContext(GrpcTestFixture<TStartup> fixture)
        {
            _executionContext = ExecutionContext.Capture()!;
            _stopwatch = Stopwatch.StartNew();
            _fixture = fixture;
            _fixture.LoggedMessage += WriteMessage;
        }

        private void WriteMessage(LogLevel logLevel, string category, EventId eventId, string message, Exception? exception)
        {
            // Log using the passed in execution context.
            // In the case of NUnit, console output is only captured by the test
            // if it is written in the test's execution context.
            ExecutionContext.Run(_executionContext, s =>
            {
                Console.WriteLine($"{_stopwatch.Elapsed.TotalSeconds:N3}s {category} - {logLevel}: {message}");
            }, null);
        }

        public void Dispose()
        {
            _fixture.LoggedMessage -= WriteMessage;
            _executionContext?.Dispose();
        }
    }
}
