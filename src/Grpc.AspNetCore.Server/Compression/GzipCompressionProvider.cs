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

using System.IO;
using System.IO.Compression;

namespace Grpc.AspNetCore.Server.Compression
{
    /// <summary>
    /// GZIP compression provider.
    /// </summary>
    public class GzipCompressionProvider : ICompressionProvider
    {
        private readonly CompressionLevel _compressionLevel;

        /// <summary>
        /// Initializes a new instance of the <see cref="GzipCompressionProvider"/> class with the specified <see cref="CompressionLevel"/>.
        /// </summary>
        /// <param name="compressionLevel">The compression level to use when compressing data.</param>
        public GzipCompressionProvider(CompressionLevel compressionLevel)
        {
            _compressionLevel = compressionLevel;
        }

        /// <summary>
        /// The encoding name used in the 'grpc-encoding' and 'grpc-accept-encoding' request and response headers.
        /// </summary>
        public string EncodingName => "gzip";

        /// <summary>
        /// Create a new compression stream.
        /// </summary>
        /// <param name="stream">
        /// The stream where the compressed data is written when <paramref name="compressionMode"/> is <c>Compress</c>,
        /// and where compressed data is copied from when <paramref name="compressionMode"/> is <c>Decompress</c>.
        /// </param>
        /// <param name="compressionMode">The compression mode.</param>
        /// <returns>A stream used to compress or decompress data.</returns>
        public Stream CreateStream(Stream stream, CompressionMode compressionMode)
        {
            if (compressionMode == CompressionMode.Compress)
            {
                return new GZipStream(stream, _compressionLevel);
            }

            return new GZipStream(stream, CompressionMode.Decompress);
        }
    }
}
