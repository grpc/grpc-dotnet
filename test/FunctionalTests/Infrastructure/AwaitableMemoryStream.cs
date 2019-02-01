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
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    public class AwaitableMemoryStream : Stream
    {
        private TaskCompletionSource<byte[]> _tcs;
        private byte[] _currentData;

        public AwaitableMemoryStream()
        {
            _currentData = Array.Empty<byte>();
            ResetTcs();
        }

        private void ResetTcs()
        {
            _tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void SendData(byte[] data)
        {
            _tcs.SetResult(data);
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // Still have leftover data?
            if (_currentData.Length > 0)
            {
                return ReadInternalBuffer(buffer, offset, count);
            }

            _currentData = await _tcs.Task;
            ResetTcs();

            return ReadInternalBuffer(buffer, offset, count);
        }

        private int ReadInternalBuffer(byte[] buffer, int offset, int count)
        {
            var readBytes = Math.Min(count, _currentData.Length);
            if (readBytes > 0)
            {
                Array.Copy(_currentData, 0, buffer, offset, readBytes);
                _currentData = _currentData.AsSpan(readBytes, _currentData.Length - readBytes).ToArray();
            }

            return readBytes;
        }

        #region Stream implementation
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => throw new NotImplementedException();

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return 0;
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
