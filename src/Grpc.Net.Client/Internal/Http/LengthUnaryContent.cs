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
    internal class LengthUnaryContent<TRequest, TResponse> : HttpContent
        where TRequest : class
        where TResponse : class
    {
        private readonly TRequest _content;
        private readonly GrpcCall<TRequest, TResponse> _call;
        private byte[]? _payload;

        public LengthUnaryContent(TRequest content, GrpcCall<TRequest, TResponse> call, MediaTypeHeaderValue mediaType)
        {
            _content = content;
            _call = call;
            Headers.ContentType = mediaType;
        }

        // Serialize message. Need to know size to prefix the length in the header.
        private byte[] SerializePayload()
        {
            var serializationContext = _call.SerializationContext;
            serializationContext.CallOptions = _call.Options;
            serializationContext.Initialize();

            try
            { 
                _call.Method.RequestMarshaller.ContextualSerializer(_content, serializationContext);

                return serializationContext.GetWrittenPayload().ToArray();
            }
            finally
            {
                serializationContext.Reset();
            }
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            if (_payload == null)
            {
                _payload = SerializePayload();
            }

            await _call.WriteMessageAsync(
                stream,
                _payload,
                _call.Options).ConfigureAwait(false);

            GrpcEventSource.Log.MessageSent();
        }

        protected override bool TryComputeLength(out long length)
        {
            if (_payload == null)
            {
                _payload = SerializePayload();
            }

            length = _payload.Length;
            return true;
        }
    }
}
