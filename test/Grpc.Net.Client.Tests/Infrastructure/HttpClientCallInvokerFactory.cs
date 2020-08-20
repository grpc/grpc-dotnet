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
using System.Net.Http;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Tests.Infrastructure
{
    internal static class HttpClientCallInvokerFactory
    {
        public static HttpClientCallInvoker Create(
            HttpClient httpClient,
            ILoggerFactory? loggerFactory = null,
            ISystemClock? systemClock = null,
            Action<GrpcChannelOptions>? configure = null,
            bool? disableClientDeadline = null,
            long? maxTimerPeriod = null)
        {
            var channelOptions = new GrpcChannelOptions
            {
                LoggerFactory = loggerFactory,
                HttpClient = httpClient
            };
            configure?.Invoke(channelOptions);

            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, channelOptions);
            channel.Clock = systemClock ?? SystemClock.Instance;
            if (disableClientDeadline != null)
            {
                channel.DisableClientDeadline = disableClientDeadline.Value;
            }
            if (maxTimerPeriod != null)
            {
                channel.MaxTimerDueTime = maxTimerPeriod.Value;
            }

            return new HttpClientCallInvoker(channel);
        }
    }
}
