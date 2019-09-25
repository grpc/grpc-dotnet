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
using Microsoft.Net.Http.Headers;

namespace Grpc.AspNetCore.Server.Internal
{
    internal static class GrpcProtocolConstants
    {
        internal const string GrpcContentType = "application/grpc";
        internal const string Http2Protocol = "HTTP/2"; // This is what Kestrel sets
        internal const string Http20Protocol = "HTTP/2.0"; // This is what IIS sets

        internal const string TimeoutHeader = "grpc-timeout";
        internal const string MessageEncodingHeader = "grpc-encoding";
        internal const string MessageAcceptEncodingHeader = "grpc-accept-encoding";

        internal const string CompressionRequestAlgorithmHeader = "grpc-internal-encoding-request";

        internal const string StatusTrailer = "grpc-status";
        internal const string MessageTrailer = "grpc-message";

        internal const string IdentityGrpcEncoding = "identity";
        internal const int ResetStreamNoError = 0;

        internal static readonly HashSet<string> FilteredHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MessageEncodingHeader,
            MessageAcceptEncodingHeader,
            TimeoutHeader,
            HeaderNames.ContentType,
            HeaderNames.TE,
            HeaderNames.Host,
            HeaderNames.AcceptEncoding
        };

        internal const string X509SubjectAlternativeNameId = "2.5.29.17";
        internal const string X509SubjectAlternativeNameKey = "x509_subject_alternative_name";
        internal const string X509CommonNameKey = "x509_common_name";
    }
}
