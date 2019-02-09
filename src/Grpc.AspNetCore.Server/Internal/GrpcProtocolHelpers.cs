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
using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class GrpcProtocolHelpers
    {
        public static TimeSpan GetTimeout(HttpContext httpContext)
        {
            const long TicksPerMicrosecond = 10; // 1 microsecond = 10 ticks
            const long NanosecondsPerTick = 100; // 1 nanosecond = 0.01 ticks

            if (!httpContext.Request.Headers.TryGetValue(GrpcProtocolConstants.TimeoutHeader, out var values))
            {
                return TimeSpan.MaxValue;
            }

            if (values.Count == 1)
            {
                var timeout = values.ToString();
                if (timeout.Length >= 2)
                {
                    var timeoutUnit = timeout[timeout.Length - 1];
                    if (int.TryParse(timeout.AsSpan(0, timeout.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var timeoutValue))
                    {
                        switch (timeoutUnit)
                        {
                            case 'H':
                                return TimeSpan.FromHours(timeoutValue);
                            case 'M':
                                return TimeSpan.FromMinutes(timeoutValue);
                            case 'S':
                                return TimeSpan.FromSeconds(timeoutValue);
                            case 'm':
                                return TimeSpan.FromMilliseconds(timeoutValue);
                            case 'u':
                                return TimeSpan.FromTicks(timeoutValue * TicksPerMicrosecond);
                            case 'n':
                                return TimeSpan.FromTicks(timeoutValue / NanosecondsPerTick);
                        }
                    }
                }
            }

            throw new InvalidOperationException("Error reading grpc-timeout value.");
        }
    }
}
