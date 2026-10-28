#region Copyright notice and license

// Copyright 2026 The gRPC Authors
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

using Grpc.Net.Compression;

namespace Grpc.Tests.Shared;

internal sealed class TestCompressionProvider : ICompressionProvider
{
    private readonly Stream? _decompressionStream;

    public TestCompressionProvider(Stream? decompressionStream)
    {
        _decompressionStream = decompressionStream;
    }

    public string EncodingName => "test";
    public bool DecompressionStreamCreated { get; private set; }

    public Stream CreateCompressionStream(Stream stream, System.IO.Compression.CompressionLevel? compressionLevel)
    {
        throw new NotImplementedException();
    }

    public Stream CreateDecompressionStream(Stream stream)
    {
        DecompressionStreamCreated = true;
        return _decompressionStream ?? throw new InvalidOperationException("Decompressor must not be called.");
    }
}
