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

using System.Buffers;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Grpc.Shared;

namespace Grpc.Net.Client.Web.Internal;

// This duplicates compression logic from Grpc.Net.Client and Grpc.Net.Common. Required because this project doesn't depend on them.
internal static class CompressionHelpers
{
    internal const string MessageEncodingHeader = "grpc-encoding";
    internal const string IdentityGrpcEncoding = "identity";

    internal static readonly Dictionary<string, Func<Stream, Stream>> CompressionHandlers = new Dictionary<string, Func<Stream, Stream>>(StringComparer.Ordinal)
    {
        ["gzip"] = stream => new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true),
#if NET6_0_OR_GREATER
        ["deflate"] = stream => new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true),
#endif
    };

    public static bool TryDecompressMessage(string compressionEncoding, Dictionary<string, Func<Stream, Stream>> compressionProviders, Memory<byte> messageData, int length, out ReadOnlySequence<byte> result)
    {
        if (compressionProviders.TryGetValue(compressionEncoding, out var compressionProvider))
        {
            if (!MemoryMarshal.TryGetArray<byte>(messageData, out var arraySegment))
            {
                arraySegment = new ArraySegment<byte>(messageData.Slice(0, length).ToArray());
            }

            var output = new MemoryStream();
            using (var compressionStream = compressionProvider(new MemoryStream(arraySegment.Array!, 0, length, writable: true, publiclyVisible: true)))
            {
                compressionStream.CopyTo(output);
            }

            result = new ReadOnlySequence<byte>(output.GetBuffer(), 0, (int)output.Length);
            return true;
        }

        result = default;
        return false;
    }

    internal static string GetGrpcEncoding(HttpResponseMessage response)
    {
        var grpcEncoding = HttpRequestHelpers.GetHeaderValue(
            response.Headers,
            MessageEncodingHeader,
            first: true);

        return grpcEncoding ?? IdentityGrpcEncoding;
    }
}
