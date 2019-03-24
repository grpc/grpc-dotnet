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
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.NetCore.HttpClient
{
    internal class PipeClientStreamWriter<TRequest> : IClientStreamWriter<TRequest>
    {
        private readonly PipeWriter _writer;
        private readonly Func<TRequest, byte[]> _serializer;

        public PipeClientStreamWriter(PipeWriter Writer, Func<TRequest, byte[]> serializer, WriteOptions options)
        {
            _writer = Writer;
            _serializer = serializer;
            WriteOptions = options;
        }

        public WriteOptions WriteOptions { get; set; }

        public Task CompleteAsync()
        {
            _writer.Complete();
            return Task.CompletedTask;
        }

        public Task WriteAsync(TRequest message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            return _writer.WriteMessageCoreAsync(_serializer(message), flush: true);
        }
    }
}
