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
using System.Text.RegularExpressions;

namespace Grpc.AspNetCore.FunctionalTests.Linker.Helpers
{
    public class WebsiteProcess : DotNetProcess
    {
        private static readonly Regex NowListeningRegex = new Regex(@"^\s*Now listening on: .*:(?<port>\d*)$");

        private readonly TaskCompletionSource<object?> _startTcs;

        public string? ServerPort { get; private set; }
        public bool IsReady => _startTcs.Task.IsCompletedSuccessfully;

        public WebsiteProcess()
        {
            Process.OutputDataReceived += Process_OutputDataReceived;

            _startTcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public Task WaitForReadyAsync()
        {
            if (Process.HasExited)
            {
                return Task.FromException(new InvalidOperationException("Server is not running."));
            }

            return _startTcs.Task;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            var data = e.Data;
            if (data != null)
            {
                var m = NowListeningRegex.Match(data);
                if (m.Success)
                {
                    ServerPort = m.Groups["port"].Value;
                }

                if (data.Contains("Application started. Press Ctrl+C to shut down."))
                {
                    _startTcs.TrySetResult(null);
                }
            }
        }
    }
}
