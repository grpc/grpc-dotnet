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
using Grpc.Net.Compression;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    public class TestCompressionProvider : ICompressionProvider
    {
        public const string Name = "test-provider";

        public string EncodingName => Name;

        public Stream CreateCompressionStream(Stream stream, CompressionLevel? compressionLevel)
        {
            return new WrapperStream(stream);
        }

        public Stream CreateDecompressionStream(Stream stream)
        {
            return new WrapperStream(stream);
        }

        // Returned stream is disposed. Wrapper leaves the inner stream open.
        private class WrapperStream : Stream
        {
            private readonly Stream _innerStream;

            public WrapperStream(Stream innerStream)
            {
                _innerStream = innerStream;
            }

            public override bool CanRead => _innerStream.CanRead;
            public override bool CanSeek => _innerStream.CanSeek;
            public override bool CanWrite => _innerStream.CanWrite;
            public override long Length => _innerStream.Length;
            public override long Position
            {
                get => _innerStream.Position;
                set => _innerStream.Position = value;
            }

            public override void Flush() => _innerStream.Flush();
            public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
            public override void SetLength(long value) => _innerStream.SetLength(value);
            public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
        }
    }
}
