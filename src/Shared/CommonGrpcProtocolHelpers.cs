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
using System.Text;

namespace Grpc.Shared
{
    internal static class CommonGrpcProtocolHelpers
    {
        // Timer and DateTime.UtcNow have a 14ms precision. Add a small delay when scheduling deadline
        // timer that tests if exceeded or not. This avoids rescheduling the deadline callback multiple
        // times when timer is triggered before DateTime.UtcNow reports the deadline has been exceeded.
        // e.g.
        // - The deadline callback is raised and there is 0.5ms until deadline.
        // - The timer is rescheduled to run in 0.5ms.
        // - The deadline callback is raised again and there is now 0.4ms until deadline.
        // - The timer is rescheduled to run in 0.4ms, etc.
        private static readonly int TimerEpsilonMilliseconds = 7;

        public static long GetTimerDueTime(TimeSpan timeout, long maxTimerDueTime)
        {
            // Timer has a maximum allowed due time.
            // The called method will rechedule the timer if the deadline time has not passed.
            var dueTimeMilliseconds = timeout.Ticks / TimeSpan.TicksPerMillisecond;

            // Add epislon to take into account Timer precision.
            // This will avoid rescheduling the timer multiple times, but means deadline
            // might run slightly longer than requested.
            dueTimeMilliseconds += TimerEpsilonMilliseconds;

            dueTimeMilliseconds = Math.Min(dueTimeMilliseconds, maxTimerDueTime);
            // Timer can't have a negative due time
            dueTimeMilliseconds = Math.Max(dueTimeMilliseconds, 0);

            return dueTimeMilliseconds;
        }

        public static bool IsContentType(string contentType, string? s)
        {
            if (s == null)
            {
                return false;
            }

            if (!s.StartsWith(contentType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (s.Length == contentType.Length)
            {
                // Exact match
                return true;
            }

            // Support variations on the content-type (e.g. +proto, +json)
            var nextChar = s[contentType.Length];
            if (nextChar == ';')
            {
                return true;
            }
            if (nextChar == '+')
            {
                // Accept any message format. Marshaller could be set to support third-party formats
                return true;
            }

            return false;
        }

        public static string ConvertToRpcExceptionMessage(Exception ex)
        {
            // RpcException doesn't allow for an inner exception. To ensure the user is getting enough information about the
            // error we will concatenate any inner exception messages together.
            return ex.InnerException == null ? $"{ex.GetType().Name}: {ex.Message}" : BuildErrorMessage(ex);
        }

        private static string BuildErrorMessage(Exception ex)
        {
            // Concatenate inner exceptions messages together.
            var sb = new StringBuilder();
            var first = true;
            Exception? current = ex;
            do
            {
                if (!first)
                {
                    sb.Append(" ");
                }
                else
                {
                    first = false;
                }
                sb.Append(current.GetType().Name);
                sb.Append(": ");
                sb.Append(current.Message);
            }
            while ((current = current.InnerException) != null);

            return sb.ToString();
        }
    }
}
