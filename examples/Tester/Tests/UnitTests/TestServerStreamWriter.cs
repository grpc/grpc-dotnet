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
using System.Threading.Tasks;
using Grpc.Core;

namespace Tests.UnitTests
{
    public class TestServerStreamWriter<T> : IServerStreamWriter<T>
    {
        private readonly ServerCallContext _serverCallContext;
        private readonly Action<T>? _writeCallback;

        public IList<T> Messages { get; }

        public WriteOptions? WriteOptions { get; set; }

        public TestServerStreamWriter(ServerCallContext serverCallContext, Action<T>? writeCallback = null)
        {
            Messages = new List<T>();
            _serverCallContext = serverCallContext;
            _writeCallback = writeCallback;
        }

        public Task WriteAsync(T message)
        {
            if (_serverCallContext.CancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(_serverCallContext.CancellationToken);
            }

            Messages.Add(message);
            _writeCallback?.Invoke(message);
            return Task.CompletedTask;
        }
    }
}
