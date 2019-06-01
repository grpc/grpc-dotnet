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

using Grpc.Core;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class StatusCodeExtensions
    {
        public static string ToTrailerString(this StatusCode status)
        {
            switch (status)
            {
                case StatusCode.OK:
                    return "0";
                case StatusCode.Cancelled:
                    return "1";
                case StatusCode.Unknown:
                    return "2";
                case StatusCode.InvalidArgument:
                    return "3";
                case StatusCode.DeadlineExceeded:
                    return "4";
                case StatusCode.NotFound:
                    return "5";
                case StatusCode.AlreadyExists:
                    return "6";
                case StatusCode.PermissionDenied:
                    return "7";
                case StatusCode.ResourceExhausted:
                    return "8";
                case StatusCode.FailedPrecondition:
                    return "9";
                case StatusCode.Aborted:
                    return "10";
                case StatusCode.OutOfRange:
                    return "11";
                case StatusCode.Unimplemented:
                    return "12";
                case StatusCode.Internal:
                    return "13";
                case StatusCode.Unavailable:
                    return "14";
                case StatusCode.DataLoss:
                    return "15";
                case StatusCode.Unauthenticated:
                    return "16";
                default:
                    return status.ToString("D");
            }
        }
    }
}
