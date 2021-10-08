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
using System.IO;
using System.Net.Sockets;
using Grpc.Core;

namespace Grpc.Shared
{
    internal static class HttpExceptionResolver
    {
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
                else if (current is IOException)
                {
                    // IOException happens if there is a protocol mismatch.
                    hasIOException = true;
                }
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
            } while ((current = current.InnerException) != null);

            if (statusCode == null && (hasSocketException || hasIOException))
            {
                statusCode = StatusCode.Unavailable;
            }

            return statusCode ?? StatusCode.Internal;

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP3_0_OR_GREATER
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
    }
}
