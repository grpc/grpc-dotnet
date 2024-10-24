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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Grpc.AspNetCore.Server.Internal;

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

    public static bool IsInvalidContentType(HttpContext httpContext, [NotNullWhen(true)] out string? error)
    {
        if (httpContext.Request.ContentType == null)
        {
            error = "Content-Type is missing from the request.";
            return true;
        }
        else if (!CommonGrpcProtocolHelpers.IsContentType(GrpcProtocolConstants.GrpcContentType, httpContext.Request.ContentType))
        {
            error = $"Content-Type '{httpContext.Request.ContentType}' is not supported.";
            return true;
        }

        error = null;
        return false;
    }

    public static bool IsCorsPreflightRequest(HttpContext httpContext)
    {
        return HttpMethods.IsOptions(httpContext.Request.Method) &&
            httpContext.Request.Headers.ContainsKey(HeaderNames.AccessControlRequestMethod);
    }

    public static void BuildHttpErrorResponse(HttpResponse response, int httpStatusCode, StatusCode grpcStatusCode, string message)
    {
        response.StatusCode = httpStatusCode;
        SetStatus(GetTrailersDestination(response), new Status(grpcStatusCode, message));
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
        response.ContentType = GrpcProtocolConstants.GrpcContentType;
    }

    public static void SetStatus(IHeaderDictionary destination, Status status)
    {
        // Overwrite any previously set status
        destination[GrpcProtocolConstants.StatusTrailer] = status.StatusCode.ToTrailerString();

        string? escapedDetail;
        if (!string.IsNullOrEmpty(status.Detail))
        {
            // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#responses
            // The value portion of Status-Message is conceptually a Unicode string description of the error,
            // physically encoded as UTF-8 followed by percent-encoding.
            escapedDetail = PercentEncodingHelpers.PercentEncode(status.Detail);
        }
        else
        {
            escapedDetail = null;
        }

        destination[GrpcProtocolConstants.MessageTrailer] = escapedDetail;
    }

    public static IHeaderDictionary GetTrailersDestination(HttpResponse response)
    {
        if (response.HasStarted)
        {
            // The response has content so write trailers to a trailing HEADERS frame
            var feature = response.HttpContext.Features.Get<IHttpResponseTrailersFeature>();
            if (feature?.Trailers == null || feature.Trailers.IsReadOnly)
            {
                throw new InvalidOperationException("Trailers are not supported for this response. The server may not support gRPC.");
            }

            return feature.Trailers;
        }
        else
        {
            // The response is "Trailers-Only". There are no gRPC messages in the response so the status
            // and other trailers can be placed in the header HEADERS frame
            return response.Headers;
        }
    }

    public static AuthContext CreateAuthContext(X509Certificate2 clientCertificate)
    {
        // Map X509Certificate2 values to AuthContext. The name/values come BoringSSL via C Core
        // https://github.com/grpc/grpc/blob/a3cc5361e6f6eb679ccf5c36ecc6d0ca41b64f4f/src/core/lib/security/security_connector/ssl_utils.cc#L206-L248

        var properties = new Dictionary<string, List<AuthProperty>>(StringComparer.Ordinal);

        string? peerIdentityPropertyName = null;

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
            ref var values = ref CollectionsMarshal.GetValueRefOrAddDefault(properties, name, out _);
            values ??= [];

            values.Add(AuthProperty.Create(name, Encoding.UTF8.GetBytes(value)));
        }
    }

    internal static bool CanWriteCompressed(WriteOptions? writeOptions)
    {
        if (writeOptions == null)
        {
            return true;
        }

        var canCompress = (writeOptions.Flags & WriteFlags.NoCompress) != WriteFlags.NoCompress;

        return canCompress;
    }

    internal static bool ShouldSkipHeader(string name)
    {
        return name.StartsWith(':') || GrpcProtocolConstants.FilteredHeaders.Contains(name);
    }

    internal static IHttpRequestLifetimeFeature GetRequestLifetimeFeature(HttpContext httpContext)
    {
        var lifetimeFeature = httpContext.Features.Get<IHttpRequestLifetimeFeature>();
        if (lifetimeFeature is null)
        {
            // This should only run in tests where the HttpContext is manually created.
            lifetimeFeature = new HttpRequestLifetimeFeature();
            httpContext.Features.Set(lifetimeFeature);
        }

        return lifetimeFeature;
    }
}
