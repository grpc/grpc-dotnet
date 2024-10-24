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

namespace Grpc.Net.Client.Internal.Retry;

internal sealed partial class ChannelRetryThrottling
{
    private readonly object _lock = new object();
    private readonly double _tokenRatio;
    private readonly int _maxTokens;
    private readonly ILogger _logger;

    private double _tokenCount;
    private readonly double _tokenThreshold;
    private bool _isRetryThrottlingActive;

    public ChannelRetryThrottling(int maxTokens, double tokenRatio, ILoggerFactory loggerFactory)
    {
        // Truncate token ratio to 3 decimal places
        // https://github.com/grpc/proposal/blob/master/A6-client-retries.md#validation-of-retrythrottling
        _tokenRatio = Math.Truncate(tokenRatio * 1000) / 1000;

        _maxTokens = maxTokens;
        _tokenCount = maxTokens;
        _tokenThreshold = _tokenCount / 2;
        _logger = loggerFactory.CreateLogger(typeof(ChannelRetryThrottling));
    }

    public bool IsRetryThrottlingActive()
    {
        lock (_lock)
        {
            return _isRetryThrottlingActive;
        }
    }

    public void CallSuccess()
    {
        lock (_lock)
        {
            _tokenCount = Math.Min(_tokenCount + _tokenRatio, _maxTokens);
            UpdateRetryThrottlingActive();
        }
    }

    public void CallFailure()
    {
        lock (_lock)
        {
            _tokenCount = Math.Max(_tokenCount - 1, 0);
            UpdateRetryThrottlingActive();
        }
    }

    private void UpdateRetryThrottlingActive()
    {
        Debug.Assert(Monitor.IsEntered(_lock));

        var newRetryThrottlingActive = _tokenCount <= _tokenThreshold;

        if (newRetryThrottlingActive != _isRetryThrottlingActive)
        {
            _isRetryThrottlingActive = newRetryThrottlingActive;
            Log.RetryThrottlingActiveChanged(_logger, _isRetryThrottlingActive);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Trace, EventId = 1, EventName = "RetryThrottlingActiveChanged", Message = "Retry throttling active state changed. New value: {RetryThrottlingActive}")]
        public static partial void RetryThrottlingActiveChanged(ILogger logger, bool retryThrottlingActive);
    }
}
