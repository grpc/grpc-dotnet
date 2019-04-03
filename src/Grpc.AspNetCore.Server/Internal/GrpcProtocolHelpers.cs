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
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Server.Compression;
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

        public static Task SendHttpError(HttpResponse response, int httpStatusCode, StatusCode grpcStatusCode, string message)
        {
            response.StatusCode = httpStatusCode;
            response.AppendTrailer(GrpcProtocolConstants.StatusTrailer, grpcStatusCode.ToTrailerString());
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

        internal static bool TryDecompressMessage(string compressionEncoding, List<ICompressionProvider> compressionProviders, byte[] messageData, out byte[] result)
        {
            foreach (var compressionProvider in compressionProviders)
            {
                if (string.Equals(compressionEncoding, compressionProvider.EncodingName, StringComparison.Ordinal))
                {
                    var output = new MemoryStream();
                    var compressionStream = compressionProvider.CreateDecompressionStream(new MemoryStream(messageData));
                    compressionStream.CopyTo(output);

                    result = output.ToArray();
                    return true;
                }
            }

            result = null;
            return false;
        }

        internal static byte[] CompressMessage(string compressionEncoding, CompressionLevel? compressionLevel, List<ICompressionProvider> compressionProviders, byte[] messageData)
        {
            foreach (var compressionProvider in compressionProviders)
            {
                if (string.Equals(compressionEncoding, compressionProvider.EncodingName, StringComparison.Ordinal))
                {
                    var output = new MemoryStream();
                    using (var compressionStream = compressionProvider.CreateCompressionStream(output, compressionLevel))
                    {
                        compressionStream.Write(messageData, 0, messageData.Length);
                    }

                    return output.ToArray();
                }
            }

            // Should never reach here
            throw new InvalidOperationException($"Could not find compression provider for '{compressionEncoding}'.");
        }

        public static void AddProtocolHeaders(HttpResponse response)
        {
            response.ContentType = GrpcProtocolConstants.GrpcContentType;
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

        public static AuthContext CreateAuthContext(X509Certificate2 clientCertificate)
        {
            // Map X509Certificate2 values to AuthContext. The name/values come BoringSSL via C Core
            // https://github.com/grpc/grpc/blob/a3cc5361e6f6eb679ccf5c36ecc6d0ca41b64f4f/src/core/lib/security/security_connector/ssl_utils.cc#L206-L248

            var properties = new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal);

            string peerIdentityPropertyName = null;

            var dnsNames = X509CertificateHelpers.GetDnsFromExtensions(clientCertificate);
            foreach (var dnsName in dnsNames)
            {
                AddProperty(properties, GrpcProtocolConstants.X509SubjectAlternativeNameKey, dnsName);

                if (peerIdentityPropertyName == null)
                {
                    peerIdentityPropertyName = GrpcProtocolConstants.X509SubjectAlternativeNameKey;
                }
            }

            var commonName = clientCertificate.GetNameInfo(X509NameType.SimpleName, false);
            if (commonName != null)
            {
                AddProperty(properties, GrpcProtocolConstants.X509CommonNameKey, commonName);
                if (peerIdentityPropertyName == null)
                {
                    peerIdentityPropertyName = GrpcProtocolConstants.X509CommonNameKey;
                }
            }

            return new AuthContext(peerIdentityPropertyName, properties);

            static void AddProperty(Dictionary<string, List<AuthProperty>> properties, string name, string value)
            {
                if (!properties.TryGetValue(name, out var values))
                {
                    values = new List<AuthProperty>();
                    properties[name] = values;
                }

                values.Add(AuthProperty.Create(name, Encoding.UTF8.GetBytes(value)));
            }
        }
    }
}
