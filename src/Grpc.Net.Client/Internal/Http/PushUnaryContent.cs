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

using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

#if NETSTANDARD2_0
using ValueTask = System.Threading.Tasks.Task;
#endif

namespace Grpc.Net.Client.Internal.Http
{
    internal class PushUnaryContent<TRequest, TResponse> : HttpContent
        where TRequest : class
        where TResponse : class
    {
        private readonly TRequest _content;
        private readonly GrpcCall<TRequest, TResponse> _call;

        public PushUnaryContent(TRequest content, GrpcCall<TRequest, TResponse> call, MediaTypeHeaderValue mediaType)
        {
            _content = content;
            _call = call;
            Headers.ContentType = mediaType;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
#pragma warning disable CA2012 // Use ValueTasks correctly
            var writeMessageTask = _call.WriteMessageAsync(
                stream,
                _content,
                _call.Method.RequestMarshaller.ContextualSerializer,
                _call.Options);
#pragma warning restore CA2012 // Use ValueTasks correctly
            if (writeMessageTask.IsCompletedSuccessfully())
            {
                GrpcEventSource.Log.MessageSent();
                return Task.CompletedTask;
            }

            return WriteMessageCore(writeMessageTask);
        }

        private static async Task WriteMessageCore(ValueTask writeMessageTask)
        {
            await writeMessageTask.ConfigureAwait(false);
            GrpcEventSource.Log.MessageSent();
        }

        protected override bool TryComputeLength(out long length)
        {
            // We can't know the length of the content being pushed to the output stream.
            length = -1;
            return false;
        }
    }
}
