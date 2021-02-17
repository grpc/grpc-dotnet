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
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Grpc.Core;
using Grpc.Shared;
using Microsoft.Extensions.Logging;

namespace Grpc.Net.Client.Internal
{
    internal abstract class GrpcCall
    {
        // Getting logger name from generic type is slow
        private const string LoggerName = "Grpc.Net.Client.Internal.GrpcCall";

        private GrpcCallSerializationContext? _serializationContext;
        private DefaultDeserializationContext? _deserializationContext;

        protected Metadata? Trailers { get; set; }

        public bool ResponseFinished { get; protected set; }
        public HttpResponseMessage? HttpResponse { get; protected set; }

        public GrpcCallSerializationContext SerializationContext
        {
            get { return _serializationContext ??= new GrpcCallSerializationContext(this); }
        }

        public DefaultDeserializationContext DeserializationContext
        {
            get { return _deserializationContext ??= new DefaultDeserializationContext(); }
        }

        public CallOptions Options { get; }
        public ILogger Logger { get; }
        public GrpcChannel Channel { get; }

        public string? RequestGrpcEncoding { get; internal set; }

        public abstract Type RequestType { get; }
        public abstract Type ResponseType { get; }

        protected GrpcCall(CallOptions options, GrpcChannel channel)
        {
            Options = options;
            Channel = channel;
            Logger = channel.LoggerFactory.CreateLogger(LoggerName);
        }

        internal RpcException CreateRpcException(Status status)
        {
            TryGetTrailers(out var trailers);
            return new RpcException(status, trailers ?? Metadata.Empty);
        }

        protected bool TryGetTrailers([NotNullWhen(true)] out Metadata? trailers)
        {
            if (Trailers == null)
            {
                // Trailers are read from the end of the request.
                // If the request isn't finished then we can't get the trailers.
                if (!ResponseFinished)
                {
                    trailers = null;
                    return false;
                }

                CompatibilityExtensions.Assert(HttpResponse != null);
                Trailers = GrpcProtocolHelpers.BuildMetadata(HttpResponse.TrailingHeaders());
            }

            trailers = Trailers;
            return true;
        }
    }
}
