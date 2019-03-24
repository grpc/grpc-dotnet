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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.NetCore.HttpClient
{
    internal class ClientAsyncStreamReader<TResponse> : IAsyncStreamReader<TResponse>
    {
        private Task<HttpResponseMessage> _sendTask;
        private Stream _responseStream;
        private Func<byte[], TResponse> _deserializer;

        public ClientAsyncStreamReader(Task<HttpResponseMessage> sendTask, Func<byte[], TResponse> deserializer)
        {
            _sendTask = sendTask;
            _deserializer = deserializer;
        }

        public TResponse Current { get; private set; }

        public void Dispose()
        {
        }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            try
            {
                if (_responseStream == null)
                {
                    var responseMessage = await _sendTask;
                    _responseStream = await responseMessage.Content.ReadAsStreamAsync();
                }

                Current = _responseStream.ReadSingleMessage(_deserializer);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
