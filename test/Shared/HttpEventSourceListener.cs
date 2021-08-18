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
using System.Diagnostics.Tracing;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Grpc.Tests.Shared
{
    public sealed class HttpEventSourceListener : EventListener
    {
        private readonly StringBuilder _messageBuilder = new StringBuilder();
        private readonly ILogger _logger;

        public HttpEventSourceListener(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(nameof(HttpEventSourceListener));
        }

        protected override void OnEventSourceCreated(EventSource eventSource)
        {
            base.OnEventSourceCreated(eventSource);

            if (eventSource.Name.Contains("System.Net.Quic") ||
                eventSource.Name.Contains("System.Net.Http"))
            {
                EnableEvents(eventSource, EventLevel.LogAlways, EventKeywords.All);
            }
        }

        protected override void OnEventWritten(EventWrittenEventArgs eventData)
        {
            base.OnEventWritten(eventData);

            string message;
            lock (_messageBuilder)
            {
                _messageBuilder.Append("<- Event ");
                _messageBuilder.Append(eventData.EventSource.Name);
                _messageBuilder.Append(" - ");
                _messageBuilder.Append(eventData.EventName);
                _messageBuilder.Append(" : ");
                _messageBuilder.AppendJoin(',', eventData.Payload!);
                _messageBuilder.Append(" ->");
                message = _messageBuilder.ToString();
                _messageBuilder.Clear();
            }

            _logger.LogDebug(message);
        }

        public override string ToString()
        {
            return _messageBuilder.ToString();
        }
    }
}
