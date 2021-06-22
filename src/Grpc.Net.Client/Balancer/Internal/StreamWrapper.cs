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

#if SUPPORT_LOAD_BALANCING
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Net.Client.Balancer.Internal
{
    internal class StreamWrapper : Stream
    {
        private readonly Stream _inner;
        private readonly Action<Stream> _onDispose;

        public StreamWrapper(Stream inner, Action<Stream> onDispose)
        {
            _inner = inner;
            _onDispose = onDispose;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }
        public override int ReadTimeout
        {
            get => _inner.ReadTimeout;
            set => _inner.ReadTimeout = value;
        }
        public override int WriteTimeout
        {
            get => _inner.WriteTimeout;
            set => _inner.WriteTimeout = value;
        }

        public override void Flush() => _inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.WriteAsync(buffer, offset, count, cancellationToken);

#if !NETSTANDARD2_0
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.WriteAsync(buffer, cancellationToken);
#endif

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

#if !NETSTANDARD2_0
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
#endif

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            _inner.CopyToAsync(destination, bufferSize, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _onDispose(this);
                _inner.Dispose();
            }
        }
    }
}
#endif
