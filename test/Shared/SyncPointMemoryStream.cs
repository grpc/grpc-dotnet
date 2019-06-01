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
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.Tests.Shared
{
    /// <summary>
    /// A memory stream that waits for data when reading and allows the sender of data to wait for it to be read.
    /// </summary>
    public class SyncPointMemoryStream : Stream
    {
        private SyncPoint _syncPoint;
        private Func<Task> _awaiter;
        private byte[] _currentData;

        public SyncPointMemoryStream()
        {
            _currentData = Array.Empty<byte>();
            _awaiter = SyncPoint.Create(out _syncPoint);
        }

        /// <summary>
        /// Give the stream more data and wait until it is all read.
        /// </summary>
        public Task AddDataAndWait(byte[] data)
        {
            AddDataCore(data);
            return _awaiter();
        }

        /// <summary>
        /// Give the stream more data.
        /// </summary>
        public void AddData(byte[] data)
        {
            AddDataCore(data);
            _ = _awaiter();
        }

        private void AddDataCore(byte[] data)
        {
            if (_currentData.Length != 0)
            {
                throw new Exception("Memory stream still has data to read.");
            }

            _currentData = data;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            // Still have leftover data?
            if (_currentData.Length > 0)
            {
                return ReadInternalBuffer(buffer, offset, count);
            }

            cancellationToken.Register(() =>
            {
                _syncPoint.CancelWaitForSyncPoint(cancellationToken);
            });

            // Wait until data is provided by AddDataAndWait
            await _syncPoint.WaitForSyncPoint();

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

            if (_currentData.Length == 0)
            {
                // We have read all data
                // Signal AddDataAndWait to continue
                // Reset sync point for next read
                var syncPoint = _syncPoint;

                ResetSyncPoint();

                syncPoint.Continue();
            }

            return readBytes;
        }

        private void ResetSyncPoint()
        {
            _awaiter = SyncPoint.Create(out _syncPoint);
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
