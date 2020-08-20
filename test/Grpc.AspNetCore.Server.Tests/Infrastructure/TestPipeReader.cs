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
using System.Buffers;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Server.Tests.Infrastructure
{
    public class TestPipeReader : PipeReader
    {
        private readonly PipeReader _pipeReader;
        private ReadOnlySequence<byte> _currentBuffer;

        public long Consumed { get; set; }
        public long Examined { get; set; }

        public TestPipeReader(PipeReader pipeReader)
        {
            _pipeReader = pipeReader;
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
            Consumed += _currentBuffer.Slice(0, consumed).Length;
            Examined = Consumed;
            _pipeReader.AdvanceTo(consumed);
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            Consumed += _currentBuffer.Slice(0, consumed).Length;
            Examined = Consumed + _currentBuffer.Slice(0, examined).Length;
            _pipeReader.AdvanceTo(consumed, examined);
        }

        public override void CancelPendingRead()
        {
            _pipeReader.CancelPendingRead();
        }

        public override void Complete(Exception? exception = null)
        {
            _pipeReader.Complete(exception);
        }

        [Obsolete]
        public override void OnWriterCompleted(Action<Exception?, object> callback, object state)
        {
            _pipeReader.OnWriterCompleted(callback, state);
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            var result = await _pipeReader.ReadAsync(cancellationToken);
            _currentBuffer = result.Buffer;

            return result;
        }

        public override bool TryRead(out ReadResult result)
        {
            return _pipeReader.TryRead(out result);
        }
    }
}
