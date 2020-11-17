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
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Reflection;
using Grpc.Net.Compression;

namespace Grpc.Net.Client.Internal
{
    internal static class GrpcProtocolConstants
    {
        internal const string GrpcContentType = "application/grpc";
        internal static readonly MediaTypeHeaderValue GrpcContentTypeHeaderValue = new MediaTypeHeaderValue("application/grpc");

        internal const string TimeoutHeader = "grpc-timeout";
        internal const string MessageEncodingHeader = "grpc-encoding";

        internal const string StatusTrailer = "grpc-status";
        internal const string MessageTrailer = "grpc-message";

        internal const string IdentityGrpcEncoding = "identity";

        internal const string MessageAcceptEncodingHeader = "grpc-accept-encoding";

        internal const string CompressionRequestAlgorithmHeader = "grpc-internal-encoding-request";

        internal static readonly Dictionary<string, ICompressionProvider> DefaultCompressionProviders = new Dictionary<string, ICompressionProvider>(StringComparer.Ordinal)
        {
            ["gzip"] = new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest),
            // deflate is not supported. .NET's DeflateStream does not support RFC1950 - https://github.com/dotnet/corefx/issues/7570
        };

        internal const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        internal const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        internal static readonly string DefaultMessageAcceptEncodingValue;

        internal static readonly string UserAgentHeader;
        internal static readonly string UserAgentHeaderValue;
        internal static readonly string TEHeader;
        internal static readonly string TEHeaderValue;

        internal static string GetMessageAcceptEncoding(Dictionary<string, ICompressionProvider> compressionProviders)
        {
            return IdentityGrpcEncoding + "," +
#if !NETSTANDARD2_0
                string.Join(',', compressionProviders.Select(p => p.Key));
#else
                string.Join(",", compressionProviders.Select(p => p.Key));
#endif
        }

        static GrpcProtocolConstants()
        {
            var userAgent = "grpc-dotnet";

            // Use the assembly file version in the user agent.
            // We are not using `AssemblyInformationalVersionAttribute` because Source Link appends
            // the git hash to it, and sending a long user agent has perf implications.
            var assemblyVersion = typeof(GrpcProtocolConstants)
                .Assembly
                .GetCustomAttributes<AssemblyFileVersionAttribute>()
                .FirstOrDefault();

            Debug.Assert(assemblyVersion != null);

            // Assembly file version attribute should always be present,
            // but in case it isn't then don't include version in user-agent.
            if (assemblyVersion != null)
            {
                userAgent += "/" + assemblyVersion.Version;
            }

            UserAgentHeader = "User-Agent";
            UserAgentHeaderValue = userAgent;
            TEHeader = "TE";
            TEHeaderValue = "trailers";

            DefaultMessageAcceptEncodingValue = GetMessageAcceptEncoding(DefaultCompressionProviders);
        }
    }
}
