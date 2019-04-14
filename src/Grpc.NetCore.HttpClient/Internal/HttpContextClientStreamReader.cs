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

namespace Grpc.NetCore.HttpClient.Internal
{
    internal class HttpContextClientStreamReader<TRequest, TResponse> : IAsyncStreamReader<TResponse>
    {
        private readonly GrpcCall<TRequest, TResponse> _call;
        private Stream _responseStream;

        public HttpContextClientStreamReader(GrpcCall<TRequest, TResponse> call)
        {
            _call = call;
        }

        public TResponse Current { get; private set; }

        public void Dispose()
        {
        }

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            // HTTP response has finished
            if (_call.ResponseFinished)
            {
                return false;
            }

            // User could have disposed call
            _call.EnsureNotDisposed();

            // Linking tokens is expensive. Only create a linked token is passed in cancellation token requires it
            CancellationTokenSource cts = null;
            if (cancellationToken.CanBeCanceled)
            {
                cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _call.CancellationToken);
                cancellationToken = cts.Token;
            }
            else
            {
                cancellationToken = _call.CancellationToken;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_responseStream == null)
            {
                var responseMessage = await _call.SendTask.ConfigureAwait(false);
                _responseStream = await responseMessage.Content.ReadAsStreamAsync().ConfigureAwait(false);
            }

            using (cts)
            {
                Current = await _responseStream.ReadStreamedMessageAsync(_call.Method.ResponseMarshaller.Deserializer, cancellationToken).ConfigureAwait(false);
                if (Current == null)
                {
                    // No more content in response so mark as finished
                    _call.ResponseFinished = true;
                    return false;
                }

                return true;
            }
        }
    }
}
