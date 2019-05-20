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
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace Grpc.AspNetCore.Microbenchmarks.Internal
{
    public class TestPipeReader : PipeReader
    {
        public List<ValueTask<ReadResult>> ReadResults { get; }

        public TestPipeReader()
        {
            ReadResults = new List<ValueTask<ReadResult>>();
        }

        public override void AdvanceTo(SequencePosition consumed)
        {
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
        }

        public override void CancelPendingRead()
        {
            throw new NotImplementedException();
        }

        public override void Complete(Exception? exception = null)
        {
            throw new NotImplementedException();
        }

        public override void OnWriterCompleted(Action<Exception, object> callback, object state)
        {
            throw new NotImplementedException();
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (ReadResults.Count == 0)
            {
                return new ValueTask<ReadResult>(new ReadResult(default, false, true));
            }

            var result = ReadResults[0];
            ReadResults.RemoveAt(0);

            return result;
        }

        public override bool TryRead(out ReadResult result)
        {
            throw new NotImplementedException();
        }
    }
}
