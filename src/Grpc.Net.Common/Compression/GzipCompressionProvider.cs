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

using System.IO.Compression;

namespace Grpc.Net.Compression
{
    /// <summary>
    /// GZIP compression provider.
    /// </summary>
    public class GzipCompressionProvider : ICompressionProvider
    {
        private readonly CompressionLevel _defaultCompressionLevel;

        /// <summary>
        /// Initializes a new instance of the <see cref="GzipCompressionProvider"/> class with the specified <see cref="CompressionLevel"/>.
        /// </summary>
        /// <param name="defaultCompressionLevel">The default compression level to use when compressing data.</param>
        public GzipCompressionProvider(CompressionLevel defaultCompressionLevel)
        {
            _defaultCompressionLevel = defaultCompressionLevel;
        }

        /// <summary>
        /// The encoding name used in the 'grpc-encoding' and 'grpc-accept-encoding' request and response headers.
        /// </summary>
        public string EncodingName => "gzip";

        /// <summary>
        /// Create a new compression stream.
        /// </summary>
        /// <param name="stream">The stream that compressed data is written to.</param>
        /// <param name="compressionLevel">The compression level.</param>
        /// <returns>A stream used to compress data.</returns>
        public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel)
        {
            return new GZipStream(stream, compressionLevel ?? _defaultCompressionLevel, leaveOpen: true);
        }

        /// <summary>
        /// Create a new decompression stream.
        /// </summary>
        /// <param name="stream">The stream that compressed data is copied from.</param>
        /// <returns>A stream used to decompress data.</returns>
        public Stream CreateDecompressionStream(Stream stream)
        {
            return new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        }
    }
}
