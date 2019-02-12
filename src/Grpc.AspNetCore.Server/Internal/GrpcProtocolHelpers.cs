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
using System.Globalization;
using Microsoft.Extensions.Primitives;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class GrpcProtocolHelpers
    {
        public static bool TryDecodeTimeout(StringValues values, out TimeSpan timeout)
        {
            const long TicksPerMicrosecond = 10; // 1 microsecond = 10 ticks
            const long NanosecondsPerTick = 100; // 1 nanosecond = 0.01 ticks

            if (values.Count == 1)
            {
                var timeoutHeader = values.ToString();
                if (timeoutHeader.Length >= 2)
                {
                    var timeoutUnit = timeoutHeader[timeoutHeader.Length - 1];
                    if (int.TryParse(timeoutHeader.AsSpan(0, timeoutHeader.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var timeoutValue))
                    {
                        switch (timeoutUnit)
                        {
                            case 'H':
                                timeout = TimeSpan.FromHours(timeoutValue);
                                return true;
                            case 'M':
                                timeout = TimeSpan.FromMinutes(timeoutValue);
                                return true;
                            case 'S':
                                timeout = TimeSpan.FromSeconds(timeoutValue);
                                return true;
                            case 'm':
                                timeout = TimeSpan.FromMilliseconds(timeoutValue);
                                return true;
                            case 'u':
                                timeout = TimeSpan.FromTicks(timeoutValue * TicksPerMicrosecond);
                                return true;
                            case 'n':
                                timeout = TimeSpan.FromTicks(timeoutValue / NanosecondsPerTick);
                                return true;
                        }
                    }
                }
            }

            timeout = TimeSpan.Zero;
            return false;
        }
    }
}
