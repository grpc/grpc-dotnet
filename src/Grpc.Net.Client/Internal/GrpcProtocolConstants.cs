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

using System.Collections.Generic;
using System.Net.Http.Headers;
using Grpc.Core;
using Grpc.Net.Compression;

namespace Grpc.Net.Client.Internal;

internal static class GrpcProtocolConstants
{
#if !NETSTANDARD2_0 && !NET462
    public static readonly Version Http2Version = System.Net.HttpVersion.Version20;
#else
    public static readonly Version Http2Version = new Version(2, 0);
#endif

    internal const string GrpcContentType = "application/grpc";
    internal static readonly MediaTypeHeaderValue GrpcContentTypeHeaderValue = new MediaTypeHeaderValue("application/grpc");

    internal const string TimeoutHeader = "grpc-timeout";
    internal const string MessageEncodingHeader = "grpc-encoding";

    internal const string StatusTrailer = "grpc-status";
    internal const string MessageTrailer = "grpc-message";

    internal const string IdentityGrpcEncoding = "identity";

    internal const string MessageAcceptEncodingHeader = "grpc-accept-encoding";
    internal const string CompressionRequestAlgorithmHeader = "grpc-internal-encoding-request";

    internal const string RetryPushbackHeader = "grpc-retry-pushback-ms";
    internal const string RetryPreviousAttemptsHeader = "grpc-previous-rpc-attempts";

    internal const string DropRequestTrailer = "grpc-internal-drop-request";

    internal static readonly Dictionary<string, ICompressionProvider> DefaultCompressionProviders = new Dictionary<string, ICompressionProvider>(StringComparer.Ordinal)
    {
        ["gzip"] = new GzipCompressionProvider(System.IO.Compression.CompressionLevel.Fastest),
#if NET6_0_OR_GREATER
        ["deflate"] = new DeflateCompressionProvider(System.IO.Compression.CompressionLevel.Fastest),
#endif
    };

    internal const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
    internal const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

    internal static readonly string DefaultMessageAcceptEncodingValue;

    internal static readonly string UserAgentHeader;
    internal static readonly string UserAgentHeaderValue;
    internal static readonly string TEHeader;
    internal static readonly string TEHeaderValue;

    internal static readonly Status DeadlineExceededStatus = new Status(StatusCode.DeadlineExceeded, string.Empty);
    internal static readonly Status ThrottledStatus = new Status(StatusCode.Cancelled, "Retries stopped because retry throttling is active.");

    internal static Status CreateClientCanceledStatus(Exception? exception) => new Status(StatusCode.Cancelled, "Call canceled by the client.", exception);
    internal static Status CreateDisposeCanceledStatus(Exception? exception) => new Status(StatusCode.Cancelled, "gRPC call disposed.", exception);

    internal static string GetMessageAcceptEncoding(Dictionary<string, ICompressionProvider> compressionProviders)
    {
        return IdentityGrpcEncoding + "," +
#if !NETSTANDARD2_0 && !NET462
            string.Join(',', compressionProviders.Select(p => p.Key));
#else
            string.Join(",", compressionProviders.Select(p => p.Key));
#endif
    }

    /// <summary>
    /// Gets key value pairs used by debugging. These are provided as an enumerator instead of a dictionary
    /// because it's one method to implement an enumerator on gRPC calls compared to a dozen members for a dictionary.
    /// </summary>
    public static IEnumerator<KeyValuePair<string, object>> GetDebugEnumerator(ChannelBase channel, IMethod method, object? request)
    {
        const string MethodKey = "Method";
        const string ChannelKey = "Channel";
        const string RequestKey = "Request";

        yield return new KeyValuePair<string, object>(ChannelKey, channel);
        yield return new KeyValuePair<string, object>(MethodKey, method);
        if (request != null)
        {
            yield return new KeyValuePair<string, object>(RequestKey, request);
        }
    }

    static GrpcProtocolConstants()
    {
        UserAgentHeader = "User-Agent";
        UserAgentHeaderValue = UserAgentGenerator.GetUserAgentString();
        TEHeader = "TE";
        TEHeaderValue = "trailers";

        DefaultMessageAcceptEncodingValue = GetMessageAcceptEncoding(DefaultCompressionProviders);
    }
}
