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
using System.Net.Http.Headers;
using System.Net.Sockets;
using Grpc.Core;
using Grpc.Net.Compression;
using Grpc.Shared;

namespace Grpc.Net.Client.Internal;

internal static class GrpcProtocolHelpers
{
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
                throw new FormatException("Invalid Base-64 header value.");
        }

        return Convert.FromBase64String(decodable);
    }

    public static Metadata BuildMetadata(HttpHeaders responseHeaders)
    {
        var headers = new Metadata();

#if NET6_0_OR_GREATER
        // Use NonValidated to avoid race-conditions and because it is faster.
        foreach (var header in responseHeaders.NonValidated)
#else
        foreach (var header in responseHeaders)
#endif
        {
            if (ShouldSkipHeader(header.Key))
            {
                continue;
            }

            foreach (var value in header.Value)
            {
                if (header.Key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(header.Key, ParseBinaryHeader(value));
                }
                else
                {
                    headers.Add(header.Key, value);
                }
            }
        }

        return headers;
    }

    internal static bool ShouldSkipHeader(string name)
    {
        if (name.Length == 0)
        {
            return false;
        }

        switch (name[0])
        {
            case ':':
                // ASP.NET Core includes pseudo headers in the set of request headers
                // whereas, they are not in gRPC implementations. We will filter them
                // out when we construct the list of headers on the context.
                return true;
            case 'g':
            case 'G':
                // Exclude known grpc headers. This matches Grpc.Core client behavior.
                return string.Equals(name, GrpcProtocolConstants.StatusTrailer, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, GrpcProtocolConstants.MessageTrailer, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, GrpcProtocolConstants.MessageEncodingHeader, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, GrpcProtocolConstants.MessageAcceptEncodingHeader, StringComparison.OrdinalIgnoreCase);
            case 'c':
            case 'C':
                // Exclude known HTTP headers. This matches Grpc.Core client behavior.
                return string.Equals(name, "content-encoding", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "content-type", StringComparison.OrdinalIgnoreCase);
            default:
                return false;
        }
    }

    private const int MillisecondsPerSecond = 1000;

    /* round an integer up to the next value with three significant figures */
    private static long TimeoutRoundUpToThreeSignificantFigures(long x)
    {
        if (x < 1000)
        {
            return x;
        }
        if (x < 10000)
        {
            return RoundUp(x, 10);
        }
        if (x < 100000)
        {
            return RoundUp(x, 100);
        }
        if (x < 1000000)
        {
            return RoundUp(x, 1000);
        }
        if (x < 10000000)
        {
            return RoundUp(x, 10000);
        }
        if (x < 100000000)
        {
            return RoundUp(x, 100000);
        }
        if (x < 1000000000)
        {
            return RoundUp(x, 1000000);
        }
        return RoundUp(x, 10000000);

        static long RoundUp(long x, long divisor)
        {
            return (x / divisor + Convert.ToInt32(x % divisor != 0)) * divisor;
        }
    }

    private static string FormatTimeout(long value, char ext)
    {
        return value.ToString(CultureInfo.InvariantCulture) + ext;
    }

    private static string EncodeTimeoutSeconds(long sec)
    {
        if (sec % 3600 == 0)
        {
            return FormatTimeout(sec / 3600, 'H');
        }
        else if (sec % 60 == 0)
        {
            return FormatTimeout(sec / 60, 'M');
        }
        else
        {
            return FormatTimeout(sec, 'S');
        }
    }

    private static string EncodeTimeoutMilliseconds(long x)
    {
        x = TimeoutRoundUpToThreeSignificantFigures(x);
        if (x < MillisecondsPerSecond)
        {
            return FormatTimeout(x, 'm');
        }
        else
        {
            if (x % MillisecondsPerSecond == 0)
            {
                return EncodeTimeoutSeconds(x / MillisecondsPerSecond);
            }
            else
            {
                return FormatTimeout(x, 'm');
            }
        }
    }

    public static string EncodeTimeout(long timeout)
    {
        if (timeout <= 0)
        {
            return "1n";
        }
        else if (timeout < 1000 * MillisecondsPerSecond)
        {
            return EncodeTimeoutMilliseconds(timeout);
        }
        else
        {
            return EncodeTimeoutSeconds(timeout / MillisecondsPerSecond + Convert.ToInt32(timeout % MillisecondsPerSecond != 0));
        }
    }

    internal static string GetRequestEncoding(HttpRequestHeaders headers)
    {
        var grpcRequestEncoding = GetHeaderValue(
            headers,
            GrpcProtocolConstants.MessageEncodingHeader,
            first: true);

        return grpcRequestEncoding ?? GrpcProtocolConstants.IdentityGrpcEncoding;
    }

    internal static string GetGrpcEncoding(HttpResponseMessage response)
    {
        var grpcEncoding = GetHeaderValue(
            response.Headers,
            GrpcProtocolConstants.MessageEncodingHeader,
            first: true);

        return grpcEncoding ?? GrpcProtocolConstants.IdentityGrpcEncoding;
    }

    internal static string GetMessageAcceptEncoding(Dictionary<string, ICompressionProvider> compressionProviders)
    {
        if (compressionProviders == GrpcProtocolConstants.DefaultCompressionProviders)
        {
            return GrpcProtocolConstants.DefaultMessageAcceptEncodingValue;
        }

        return GrpcProtocolConstants.GetMessageAcceptEncoding(compressionProviders);
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

    internal static AuthInterceptorContext CreateAuthInterceptorContext(Uri baseAddress, IMethod method, CancellationToken cancellationToken)
    {
        var authority = baseAddress.Authority;
        if (baseAddress.Scheme == Uri.UriSchemeHttps && authority.EndsWith(":443", StringComparison.Ordinal))
        {
            // The service URL can be used by auth libraries to construct the "aud" fields of the JWT token,
            // so not producing serviceUrl compatible with other gRPC implementations can lead to auth failures.
            // For https and the default port 443, the port suffix should be stripped.
            // https://github.com/grpc/grpc/blob/39e982a263e5c48a650990743ed398c1c76db1ac/src/core/lib/security/transport/client_auth_filter.cc#L205
            authority = authority.Substring(0, authority.Length - 4);
        }
        var serviceUrl = baseAddress.Scheme + "://" + authority + baseAddress.AbsolutePath;
        if (!serviceUrl.EndsWith("/", StringComparison.Ordinal))
        {
            serviceUrl += "/";
        }
        serviceUrl += method.ServiceName;
        return new AuthInterceptorContext(serviceUrl, method.Name, cancellationToken);
    }

    internal static async Task ReadCredentialMetadata(
        DefaultCallCredentialsConfigurator configurator,
        GrpcChannel channel,
        HttpRequestMessage message,
        IMethod method,
        CallCredentials credentials,
        CancellationToken cancellationToken)
    {
        credentials.InternalPopulateConfiguration(configurator, null);

        if (configurator.Interceptor != null)
        {
            // Multiple auth interceptors can be called for a gRPC call.
            // These all have the same data: address, method and cancellation token.
            // Lazily allocate the context if it is needed.
            // Stored on the configurator instead of a ref parameter because ref parameters are not supported in async methods.
            configurator.CachedContext ??= CreateAuthInterceptorContext(channel.Address, method, cancellationToken);

            var metadata = new Metadata();
            await configurator.Interceptor(configurator.CachedContext, metadata).ConfigureAwait(false);

            foreach (var entry in metadata)
            {
                AddHeader(message.Headers, entry);
            }
        }

        if (configurator.CompositeCredentials != null)
        {
            // Copy credentials locally. ReadCredentialMetadata will update it.
            var compositeCredentials = configurator.CompositeCredentials;
            foreach (var callCredentials in compositeCredentials)
            {
                configurator.ResetPerCallCredentialState();
                await ReadCredentialMetadata(configurator, channel, message, method, callCredentials, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public static void AddHeader(HttpRequestHeaders headers, Metadata.Entry entry)
    {
        var value = entry.IsBinary ? Convert.ToBase64String(entry.ValueBytes) : entry.Value;
        headers.TryAddWithoutValidation(entry.Key, value);
    }

    public static string? GetHeaderValue(HttpHeaders? headers, string name, bool first = false)
    {
        if (headers == null)
        {
            return null;
        }

#if NET6_0_OR_GREATER
        if (!headers.NonValidated.TryGetValues(name, out var values))
        {
            return null;
        }

        using (var e = values.GetEnumerator())
        {
            if (!e.MoveNext())
            {
                return null;
            }

            var result = e.Current;
            if (!e.MoveNext())
            {
                return result;
            }

            if (first)
            {
                return result;
            }
        }
        throw new InvalidOperationException($"Multiple {name} headers.");
#else
        if (!headers.TryGetValues(name, out var values))
        {
            return null;
        }

        // HttpHeaders appears to always return an array, but fallback to converting values to one just in case
        var valuesArray = values as string[] ?? values.ToArray();

        switch (valuesArray.Length)
        {
            case 0:
                return null;
            case 1:
                return valuesArray[0];
            default:
                if (first)
                {
                    return valuesArray[0];
                }
                throw new InvalidOperationException($"Multiple {name} headers.");
        }
#endif
    }

    public static Status GetResponseStatus(HttpResponseMessage httpResponse, bool isBrowser, bool isWinHttp)
    {
        Status? status;
        try
        {
            if (!TryGetStatusCore(httpResponse.TrailingHeaders(), out status))
            {
                var detail = "No grpc-status found on response.";
                if (isBrowser)
                {
                    detail += " If the gRPC call is cross domain then CORS must be correctly configured. Access-Control-Expose-Headers needs to include 'grpc-status' and 'grpc-message'.";
                }
                if (isWinHttp)
                {
                    detail += " Using gRPC with WinHttp has Windows and package version requirements. See https://aka.ms/aspnet/grpc/netstandard for details.";
                }

                status = new Status(StatusCode.Cancelled, detail);
            }
        }
        catch (Exception ex)
        {
            // Handle error from parsing badly formed status
            status = new Status(StatusCode.Cancelled, ex.Message, ex);
        }

        return status.Value;
    }

    public static bool TryGetStatusCore(HttpHeaders headers, [NotNullWhen(true)] out Status? status)
    {
        var grpcStatus = GetHeaderValue(headers, GrpcProtocolConstants.StatusTrailer);

        // grpc-status is a required trailer
        if (grpcStatus == null)
        {
            status = null;
            return false;
        }

        int statusValue;
        if (!int.TryParse(grpcStatus, out statusValue))
        {
            throw new InvalidOperationException("Unexpected grpc-status value: " + grpcStatus);
        }

        // grpc-message is optional
        // Always read the gRPC message from the same headers collection as the status
        var grpcMessage = GetHeaderValue(headers, GrpcProtocolConstants.MessageTrailer);

        if (!string.IsNullOrEmpty(grpcMessage))
        {
            // https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-HTTP2.md#responses
            // The value portion of Status-Message is conceptually a Unicode string description of the error,
            // physically encoded as UTF-8 followed by percent-encoding.
            grpcMessage = Uri.UnescapeDataString(grpcMessage);
        }

        status = new Status((StatusCode)statusValue, grpcMessage ?? string.Empty);
        return true;
    }

    /// <summary>
    /// Resolve the exception from HttpClient to a gRPC status code.
    /// <param name="ex">The <see cref="Exception"/> to resolve a <see cref="StatusCode"/> from.</param>
    /// </summary>
    public static StatusCode ResolveRpcExceptionStatusCode(Exception ex)
    {
        StatusCode? statusCode = null;
        var hasIOException = false;
        var hasSocketException = false;

        var current = ex;
        do
        {
            // Grpc.Core tends to return Unavailable if there is a problem establishing the connection.
            // Additional changes here are likely required for cases when Unavailable is being returned
            // when it shouldn't be.
            if (current is SocketException)
            {
                // SocketError.ConnectionRefused happens when port is not available.
                // SocketError.HostNotFound happens when unknown host is specified.
                hasSocketException = true;
            }
#if NET7_0_OR_GREATER
            else if (current is HttpProtocolException httpProtocolException)
            {
                if (httpProtocolException.ErrorCode >= (long)Http3ErrorCode.H3_NO_ERROR)
                {
                    statusCode = MapHttp3ErrorCodeToStatus(httpProtocolException.ErrorCode);
                }
                else
                {
                    statusCode = MapHttp2ErrorCodeToStatus(httpProtocolException.ErrorCode);
                }
            }
#else
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
            else if (current.GetType().FullName == "System.Net.Http.Http2StreamException")
            {
                // Http2StreamException is private and there is no public API to get RST_STREAM error
                // code from public API. Parse error code from error message. This is the best option
                // until there is public API.
                if (TryGetProtocol(current.Message, out var e))
                {
                    statusCode = MapHttp2ErrorCodeToStatus(e);
                }
            }
#endif
#if NET6_0_OR_GREATER
            else if (current.GetType().FullName == "System.Net.Quic.QuicStreamAbortedException")
            {
                // QuicStreamAbortedException is private and there is no public API to get abort error
                // code from public API. Parse error code from error message. This is the best option
                // until there is public API.
                if (TryGetProtocol(current.Message, out var e))
                {
                    statusCode = MapHttp3ErrorCodeToStatus(e);
                }
            }
#endif
#endif
            else if (current is IOException)
            {
                // IOException happens if there is a protocol mismatch.
                hasIOException = true;
            }
        } while ((current = current.InnerException) != null);

        if (statusCode == null && (hasSocketException || hasIOException))
        {
            statusCode = StatusCode.Unavailable;
        }

        return statusCode ?? StatusCode.Internal;

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
#if !NET7_0_OR_GREATER
        static bool TryGetProtocol(string message, out long protocolError)
        {
            // Example content to parse:
            // 1. "The HTTP/2 server reset the stream. HTTP/2 error code 'CANCEL' (0x8)."
            // 2. "Stream aborted by peer (268)."
            var startIndex = CompatibilityHelpers.IndexOf(message, '(', StringComparison.Ordinal);
            var endIndex = CompatibilityHelpers.IndexOf(message, ')', StringComparison.Ordinal);
            if (startIndex != -1 && endIndex != -1 && endIndex - startIndex > 0)
            {
                var numberStyles = NumberStyles.Integer;
                var segment = message.Substring(startIndex + 1, endIndex - (startIndex + 1));
                if (segment.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    segment = segment.Substring(2);
                    numberStyles = NumberStyles.HexNumber;
                }

                if (long.TryParse(segment, numberStyles, CultureInfo.InvariantCulture, out var i))
                {
                    protocolError = i;
                    return true;
                }
            }

            protocolError = -1;
            return false;
        }
#endif

        static StatusCode MapHttp2ErrorCodeToStatus(long protocolError)
        {
            // Mapping from error codes to gRPC status codes is from the gRPC spec.
            return protocolError switch
            {
                (long)Http2ErrorCode.NO_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.PROTOCOL_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.INTERNAL_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.FLOW_CONTROL_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.SETTINGS_TIMEOUT => StatusCode.Internal,
                (long)Http2ErrorCode.STREAM_CLOSED => StatusCode.Internal,
                (long)Http2ErrorCode.FRAME_SIZE_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.REFUSED_STREAM => StatusCode.Unavailable,
                (long)Http2ErrorCode.CANCEL => StatusCode.Cancelled,
                (long)Http2ErrorCode.COMPRESSION_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.CONNECT_ERROR => StatusCode.Internal,
                (long)Http2ErrorCode.ENHANCE_YOUR_CALM => StatusCode.ResourceExhausted,
                (long)Http2ErrorCode.INADEQUATE_SECURITY => StatusCode.PermissionDenied,
                (long)Http2ErrorCode.HTTP_1_1_REQUIRED => StatusCode.Internal,
                _ => StatusCode.Internal
            };
        }
#endif
#if NET6_0_OR_GREATER
        static StatusCode MapHttp3ErrorCodeToStatus(long protocolError)
        {
            // Mapping from error codes to gRPC status codes is from the gRPC spec.
            return protocolError switch
            {
                (long)Http3ErrorCode.H3_NO_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_GENERAL_PROTOCOL_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_INTERNAL_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_STREAM_CREATION_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_CLOSED_CRITICAL_STREAM => StatusCode.Internal,
                (long)Http3ErrorCode.H3_FRAME_UNEXPECTED => StatusCode.Internal,
                (long)Http3ErrorCode.H3_FRAME_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_EXCESSIVE_LOAD => StatusCode.ResourceExhausted,
                (long)Http3ErrorCode.H3_ID_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_SETTINGS_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_MISSING_SETTINGS => StatusCode.Internal,
                (long)Http3ErrorCode.H3_REQUEST_REJECTED => StatusCode.Unavailable,
                (long)Http3ErrorCode.H3_REQUEST_CANCELLED => StatusCode.Cancelled,
                (long)Http3ErrorCode.H3_REQUEST_INCOMPLETE => StatusCode.Internal,
                (long)Http3ErrorCode.H3_MESSAGE_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_CONNECT_ERROR => StatusCode.Internal,
                (long)Http3ErrorCode.H3_VERSION_FALLBACK => StatusCode.Internal,
                _ => StatusCode.Internal
            };
        }
#endif
    }

    public static Status CreateStatusFromException(string summary, Exception ex, StatusCode? statusCode = null)
    {
        var exceptionMessage = CommonGrpcProtocolHelpers.ConvertToRpcExceptionMessage(ex);
        statusCode ??= ResolveRpcExceptionStatusCode(ex);

        return new Status(statusCode.Value, summary + " " + exceptionMessage, ex);
    }
}
