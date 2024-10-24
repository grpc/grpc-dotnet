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

using System.Net;
using Grpc.Shared;

namespace Grpc.Net.Client.Internal;

// TODO: Still need generic args?
internal sealed class PushUnaryContent<TRequest, TResponse> : HttpContent
    where TRequest : class
    where TResponse : class
{
    private readonly TRequest _request;
    private readonly Func<TRequest, Stream, Task> _startCallback;

    public PushUnaryContent(TRequest request, Func<TRequest, Stream, Task> startCallback)
    {
        _request = request;
        _startCallback = startCallback;
        Headers.ContentType = GrpcProtocolConstants.GrpcContentTypeHeaderValue;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var writeMessageTask = _startCallback(_request, stream);
        if (writeMessageTask.IsCompletedSuccessfully())
        {
            if (GrpcEventSource.Log.IsEnabled())
            {
                GrpcEventSource.Log.MessageSent();
            }
            return Task.CompletedTask;
        }

        return WriteMessageCore(writeMessageTask);
    }

    private static async Task WriteMessageCore(Task writeMessageTask)
    {
        await writeMessageTask.ConfigureAwait(false);
        if (GrpcEventSource.Log.IsEnabled())
        {
            GrpcEventSource.Log.MessageSent();
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        // We can't know the length of the content being pushed to the output stream.
        length = -1;
        return false;
    }
}
