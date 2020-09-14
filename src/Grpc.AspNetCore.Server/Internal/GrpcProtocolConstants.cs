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
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class GrpcProtocolConstants
    {
        internal const string GrpcContentType = "application/grpc";
        internal const string GrpcWebContentType = "application/grpc-web";
        internal const string GrpcWebTextContentType = "application/grpc-web-text";

#if NET5_0
        internal static readonly string Http2Protocol = HttpProtocol.Http2;
#else
        internal const string Http2Protocol = "HTTP/2";
        internal const string Http20Protocol = "HTTP/2.0"; // This is what IIS sets
#endif

#if NET5_0
        internal static readonly string TimeoutHeader = HeaderNames.GrpcTimeout;
#else
        internal const string TimeoutHeader = "grpc-timeout";
#endif

#if NET5_0
        internal static readonly string MessageEncodingHeader = HeaderNames.GrpcEncoding;
#else
        internal const string MessageEncodingHeader = "grpc-encoding";
#endif

#if NET5_0
        internal static readonly string MessageAcceptEncodingHeader = HeaderNames.GrpcAcceptEncoding;
#else
        internal const string MessageAcceptEncodingHeader = "grpc-accept-encoding";
#endif

        internal const string CompressionRequestAlgorithmHeader = "grpc-internal-encoding-request";

#if NET5_0
        internal static readonly string StatusTrailer = HeaderNames.GrpcStatus;
#else
        internal const string StatusTrailer = "grpc-status";
#endif

#if NET5_0
        internal static readonly string MessageTrailer = HeaderNames.GrpcMessage;
#else
        internal const string MessageTrailer = "grpc-message";
#endif

        internal const string IdentityGrpcEncoding = "identity";
        internal const int ResetStreamNoError = 0;

        internal static readonly HashSet<string> FilteredHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MessageEncodingHeader,
            MessageAcceptEncodingHeader,
            TimeoutHeader,
            HeaderNames.ContentEncoding,
            HeaderNames.ContentType,
            HeaderNames.TE,
            HeaderNames.Host,
            HeaderNames.AcceptEncoding
        };

        internal const string X509SubjectAlternativeNameId = "2.5.29.17";
        internal const string X509SubjectAlternativeNameKey = "x509_subject_alternative_name";
        internal const string X509CommonNameKey = "x509_common_name";

        // Maxmimum deadline of 99999999s is consistent with Grpc.Core
        // https://github.com/grpc/grpc/blob/907a1313a87723774bf59d04ed432602428245c3/src/core/lib/transport/timeout_encoding.h#L32-L34
        internal const long MaxDeadlineTicks = 99999999 * TimeSpan.TicksPerSecond;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsHttp2(string protocol)
        {
#if NET5_0
            return HttpProtocol.IsHttp2(protocol);
#else
            return protocol == Http2Protocol || protocol == Http20Protocol;
#endif
        }

        internal static bool IsGrpcEncodingIdentity(string encoding)
        {
            return ReferenceEquals(encoding, IdentityGrpcEncoding) ||
                string.Equals(encoding, IdentityGrpcEncoding, StringComparison.Ordinal);
        }
    }
}
