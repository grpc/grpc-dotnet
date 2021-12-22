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

using InteropTestsGrpcWebClient.Infrastructure;
using Microsoft.JSInterop;

namespace InteropTestsGrpcWebClient.Pages
{
    public partial class Index
    {
        private readonly object _lock = new object();
        private InteropTestInvoker? _interopTestInvoker;

        public List<Message> Messages { get; } = new List<Message>();

        public List<string> TestNames { get; } = new List<string>();

        public string? SelectedTest { get; set; }
        public bool IsTestRunning { get; set; }

        private readonly Dictionary<string, string[]> _testCases = new Dictionary<string, string[]>
        {
            ["empty_unary"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["large_unary"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["server_streaming"] = new[] { "GrpcWebText" },
            ["custom_metadata"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["special_status_message"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["unimplemented_service"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["unimplemented_method"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["client_compressed_unary"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["server_compressed_unary"] = new[] { "GrpcWebText", "GrpcWeb" },
            ["server_compressed_streaming"] = new[] { "GrpcWebText" },
            ["status_code_and_message"] = new[] { "GrpcWebText", "GrpcWeb" }
        };

        protected override Task OnInitializedAsync()
        {
            TestNames.AddRange(_testCases.Keys);
            SelectedTest = TestNames.First();

            _interopTestInvoker = new InteropTestInvoker(new PageLoggerFactory(AddMessage), _testCases);

            var objRef = DotNetObjectReference.Create(_interopTestInvoker);
            _ = JSRuntime.InvokeAsync<string>("initialTestHelper", objRef).AsTask();

            return base.OnInitializedAsync();
        }

        private async Task RunTest()
        {
            Messages.Clear();
            IsTestRunning = true;
            try
            {
                await _interopTestInvoker!.RunTestAsync("localhost", 8080, "GrpcWebText", SelectedTest!);
            }
            finally
            {
                IsTestRunning = false;
            }
        }

        private async Task RunAll()
        {
            Messages.Clear();
            IsTestRunning = true;
            try
            {
                foreach (var testName in TestNames)
                {
                    await _interopTestInvoker!.RunTestAsync("localhost", 8080, "GrpcWebText", testName);
                }
            }
            finally
            {
                IsTestRunning = false;
            }
        }

        private void AddMessage(LogLevel logLevel, string message)
        {
            _ = InvokeAsync(() =>
            {
                lock (_lock)
                {
                    Messages.Add(new Message
                    {
                        LogLevel = logLevel,
                        Content = message,
                    });
                    StateHasChanged();
                }
            });
        }
    }

    public class Message
    {
        public LogLevel LogLevel { get; set; }
        public string? Content { get; set; }
    }
}
