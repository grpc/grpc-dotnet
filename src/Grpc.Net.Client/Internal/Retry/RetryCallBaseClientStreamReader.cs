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

using System.Diagnostics;
using Grpc.Core;
using Grpc.Shared;

namespace Grpc.Net.Client.Internal.Retry;

[DebuggerDisplay("{DebuggerToString(),nq}")]
[DebuggerTypeProxy(typeof(RetryCallBaseClientStreamReader<,>.RetryCallBaseClientStreamReaderDebugView))]
internal sealed class RetryCallBaseClientStreamReader<TRequest, TResponse> : IAsyncStreamReader<TResponse>
    where TRequest : class
    where TResponse : class
{
    private readonly RetryCallBase<TRequest, TResponse> _retryCallBase;

    public RetryCallBaseClientStreamReader(RetryCallBase<TRequest, TResponse> retryCallBase)
    {
        _retryCallBase = retryCallBase;
    }

    public TResponse Current => _retryCallBase.CommitedCallTask.IsCompletedSuccessfully()
                ? _retryCallBase.CommitedCallTask.Result.ClientStreamReader!.Current
                : default!;

    public async Task<bool> MoveNext(CancellationToken cancellationToken)
    {
        var call = await _retryCallBase.CommitedCallTask.ConfigureAwait(false);
        return await call.ClientStreamReader!.MoveNext(cancellationToken).ConfigureAwait(false);
    }

    private string DebuggerToString()
    {
        return $"ReadCount = {_retryCallBase.MessagesRead}, EndOfStream = {(_retryCallBase.ResponseFinished ? "true" : "false")}";
    }

    private sealed class RetryCallBaseClientStreamReaderDebugView
    {
        private readonly RetryCallBaseClientStreamReader<TRequest, TResponse> _reader;

        public RetryCallBaseClientStreamReaderDebugView(RetryCallBaseClientStreamReader<TRequest, TResponse> reader)
        {
            _reader = reader;
        }

        public object? Call => _reader._retryCallBase.CallWrapper;
        public long ReadCount => _reader._retryCallBase.MessagesRead;
        public TResponse Current => _reader.Current;
        public bool EndOfStream => _reader._retryCallBase.ResponseFinished;
    }
}
