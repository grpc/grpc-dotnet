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
using Grpc.Core;

namespace Grpc.Net.Client.Internal.Configuration
{
    internal static class ConvertHelpers
    {
        public static string ConvertStatusCode(StatusCode statusCode)
        {
            return statusCode switch
            {
                StatusCode.OK => "OK",
                StatusCode.Cancelled => "CANCELLED",
                StatusCode.Unknown => "UNKNOWN",
                StatusCode.InvalidArgument => "INVALID_ARGUMENT",
                StatusCode.DeadlineExceeded => "DEADLINE_EXCEEDED",
                StatusCode.NotFound => "NOT_FOUND",
                StatusCode.AlreadyExists => "ALREADY_EXISTS",
                StatusCode.PermissionDenied => "PERMISSION_DENIED",
                StatusCode.Unauthenticated => "UNAUTHENTICATED",
                StatusCode.ResourceExhausted => "RESOURCE_EXHAUSTED",
                StatusCode.FailedPrecondition => "FAILED_PRECONDITION",
                StatusCode.Aborted => "ABORTED",
                StatusCode.OutOfRange => "OUT_OF_RANGE",
                StatusCode.Unimplemented => "UNIMPLEMENTED",
                StatusCode.Internal => "INTERNAL",
                StatusCode.Unavailable => "UNAVAILABLE",
                StatusCode.DataLoss => "DATA_LOSS",
                _ => throw new InvalidOperationException($"Unexpected status code: {statusCode}")
            };
        }

        public static StatusCode ConvertStatusCode(string statusCode)
        {
            return statusCode.ToUpperInvariant() switch
            {
                "OK" => StatusCode.OK,
                "CANCELLED" => StatusCode.Cancelled,
                "UNKNOWN" => StatusCode.Unknown,
                "INVALID_ARGUMENT" => StatusCode.InvalidArgument,
                "DEADLINE_EXCEEDED" => StatusCode.DeadlineExceeded,
                "NOT_FOUND" => StatusCode.NotFound,
                "ALREADY_EXISTS" => StatusCode.AlreadyExists,
                "PERMISSION_DENIED" => StatusCode.PermissionDenied,
                "UNAUTHENTICATED" => StatusCode.Unauthenticated,
                "RESOURCE_EXHAUSTED" => StatusCode.ResourceExhausted,
                "FAILED_PRECONDITION" => StatusCode.FailedPrecondition,
                "ABORTED" => StatusCode.Aborted,
                "OUT_OF_RANGE" => StatusCode.OutOfRange,
                "UNIMPLEMENTED" => StatusCode.Unimplemented,
                "INTERNAL" => StatusCode.Internal,
                "UNAVAILABLE" => StatusCode.Unavailable,
                "DATA_LOSS" => StatusCode.DataLoss,
                _ => int.TryParse(statusCode, out var number)
                    ? (StatusCode)number
                    : throw new InvalidOperationException($"Unexpected status code: {statusCode}")
            };
        }

        public static TimeSpan? ConvertDurationText(string? text)
        {
            if (text == null)
            {
                return null;
            }

            // This format is based on the Protobuf duration's JSON mapping.
            // https://github.com/protocolbuffers/protobuf/blob/35bdcabdd6a05ce9ee738ad7df8c1299d9c7fc4b/src/google/protobuf/duration.proto#L92
            //
            // Note that this is precise down to ticks. Fractions that are smaller than ticks will be lost.
            // This shouldn't matter because timers on Windows and Linux only have millisecond precision.
            if (text.Length > 0 && text[text.Length - 1] == 's' &&
                decimal.TryParse(text.Substring(0, text.Length - 1), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
            {
                try
                {
                    var ticks = (long)(seconds * TimeSpan.TicksPerSecond);
                    return TimeSpan.FromTicks(ticks);
                }
                catch (Exception ex)
                {
                    throw new FormatException($"'{text}' isn't a valid duration.", ex);
                }
            }
            else
            {
                throw new FormatException($"'{text}' isn't a valid duration.");
            }
        }

        public static string? ToDurationText(TimeSpan? value)
        {
            if (value == null)
            {
                return null;
            }

            // This format is based on the Protobuf duration's JSON mapping.
            // https://github.com/protocolbuffers/protobuf/blob/35bdcabdd6a05ce9ee738ad7df8c1299d9c7fc4b/src/google/protobuf/duration.proto#L92
            return value.GetValueOrDefault().TotalSeconds + "s";
        }
    }
}
