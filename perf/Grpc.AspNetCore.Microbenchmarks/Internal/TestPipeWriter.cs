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
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    public class TestPipeWriter : PipeWriter
    {
        // huge buffer that should be large enough for writing any content
        private readonly byte[] _buffer = new byte[10000];

        public bool ForceAsync { get; set; }

        public override void Advance(int bytes)
        {
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            return _buffer;
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            return _buffer;
        }

        public override void OnReaderCompleted(Action<Exception, object> callback, object state)
        {
            throw new NotImplementedException();
        }

        public override void CancelPendingFlush()
        {
            throw new NotImplementedException();
        }

        public override void Complete(Exception? exception = null)
        {
            throw new NotImplementedException();
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (!ForceAsync)
            {
                return default;
            }

            return new ValueTask<FlushResult>(ForceAsyncResult());
        }

        public async Task<FlushResult> ForceAsyncResult()
        {
            return await Task.FromResult<FlushResult>(default).ForceAsync();
        }
    }
}
