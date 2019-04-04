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
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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

        public static bool IsGrpcContentType(string contentType)
        {
            if (contentType == null)
            {
                return false;
            }

            if (!contentType.StartsWith(GrpcProtocolConstants.GrpcContentType, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (contentType.Length == GrpcProtocolConstants.GrpcContentType.Length)
            {
                // Exact match
                return true;
            }

            // Support variations on the content-type (e.g. +proto, +json)
            char nextChar = contentType[GrpcProtocolConstants.GrpcContentType.Length];
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

        public static bool IsValidContentType(HttpContext httpContext, out string error)
        {
            if (httpContext.Request.ContentType == null)
            {
                error = "Content-Type is missing from the request.";
                return false;
            }
            else if (!IsGrpcContentType(httpContext.Request.ContentType))
            {
                error = $"Content-Type '{httpContext.Request.ContentType}' is not supported.";
                return false;
            }

            error = null;
            return true;
        }

        public static Task SendHttpError(HttpResponse response, StatusCode statusCode, string message)
        {
            response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            response.AppendTrailer(GrpcProtocolConstants.StatusTrailer, statusCode.ToTrailerString());
            response.AppendTrailer(GrpcProtocolConstants.MessageTrailer, message);
            return response.WriteAsync(message);
        }

        public static byte[] ParseBinaryHeader(string base64)
        {
            string decodable;
            switch (base64.Length % 4)
            {
                case 0:
                    // base64 has the required padding 
                    decodable = base64;
                    break;
                case 2:
                    // 2 chars padding
                    decodable = base64 + "==";
                    break;
                case 3:
                    // 3 chars padding
                    decodable = base64 + "=";
                    break;
                default:
                    // length%4 == 1 should be illegal
                    throw new FormatException("Invalid base64 header value");
            }

            return Convert.FromBase64String(decodable);
        }

        public static void AddProtocolHeaders(HttpResponse response)
        {
            response.ContentType = "application/grpc";
            response.Headers.Append("grpc-encoding", "identity");
        }

        public static void SetStatusTrailers(HttpResponse response, Status status)
        {
            // Use SetTrailer here because we want to overwrite any that was set earlier
            SetTrailer(response, GrpcProtocolConstants.StatusTrailer, status.StatusCode.ToTrailerString());
            SetTrailer(response, GrpcProtocolConstants.MessageTrailer, status.Detail);
        }

        private static void SetTrailer(HttpResponse response, string trailerName, StringValues trailerValues)
        {
            var feature = response.HttpContext.Features.Get<IHttpResponseTrailersFeature>();
            if (feature?.Trailers == null || feature.Trailers.IsReadOnly)
            {
                throw new InvalidOperationException("Trailers are not supported for this response.");
            }

            feature.Trailers[trailerName] = trailerValues;
        }
    }
}
