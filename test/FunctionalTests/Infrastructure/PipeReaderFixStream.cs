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
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    // TODO(JunTaoLuo, JamesNK): Remove when fixed in 3.0 preview 3
    // Stream to work around StreamPipeReader issue
    // https://github.com/aspnet/AspNetCore/issues/7329
    public class PipeReaderFixStream : Stream
    {
        private readonly Stream _innerStream;
        private bool _isComplete;

        public PipeReaderFixStream(Stream innerStream)
        {
            _innerStream = innerStream;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new System.NotImplementedException();

        public override long Position { get => throw new System.NotImplementedException(); set => throw new System.NotImplementedException(); }

        public override void Flush()
        {
            throw new System.NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new System.NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new System.NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new System.NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new System.NotImplementedException();
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_isComplete)
            {
                return 0;
            }

            var read = await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);
            if (read == 0)
            {
                _isComplete = true;
            }

            return read;
        }
    }
}
