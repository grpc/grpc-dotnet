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

using Grpc.Core;
using System;
using System.Buffers;
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
    /// <summary>
    /// WinHttp doesn't support streaming request data so a length needs to be specified.
    /// This HttpContent pre-serializes the payload so it has a length available.
    /// The payload is then written directly to the request using specialized context
    /// and serializer method.
    /// </summary>
    internal class WinHttpUnaryContent<TRequest, TResponse> : HttpContent
        where TRequest : class
        where TResponse : class
    {
        private readonly TRequest _request;
        private readonly Func<TRequest, Stream, ValueTask> _startCallback;
        private readonly GrpcCall<TRequest, TResponse> _call;

        public WinHttpUnaryContent(TRequest request, Func<TRequest, Stream, ValueTask> startCallback, GrpcCall<TRequest, TResponse> call)
        {
            _request = request;
            _startCallback = startCallback;
            _call = call;
            Headers.ContentType = GrpcProtocolConstants.GrpcContentTypeHeaderValue;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
#pragma warning disable CA2012 // Use ValueTasks correctly
            var writeMessageTask = _startCallback(_request, stream);
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
            // This will serialize the request message again.
            // Consider caching serialized content if it is a problem.
            length = GetPayloadLength();
            return true;
        }

        private int GetPayloadLength()
        {
            var serializationContext = _call.SerializationContext;
            serializationContext.CallOptions = _call.Options;
            serializationContext.Initialize();

            try
            {
                _call.Method.RequestMarshaller.ContextualSerializer(_request, serializationContext);

                return serializationContext.GetWrittenPayload().Length;
            }
            finally
            {
                serializationContext.Reset();
            }
        }
    }
}
