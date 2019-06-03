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
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client.Internal;

namespace Grpc.Net.Client.Tests.Infrastructure
{
    internal static class ResponseUtils
    {
        public static HttpResponseMessage CreateResponse(HttpStatusCode statusCode) =>
            CreateResponse(statusCode, string.Empty);

        public static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string payload) =>
            CreateResponse(statusCode, new StringContent(payload));

        public static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, HttpContent payload, StatusCode? grpcStatusCode = StatusCode.OK)
        {
            payload.Headers.ContentType = GrpcProtocolConstants.GrpcContentTypeHeaderValue;

            var message = new HttpResponseMessage(statusCode)
            {
                Content = payload
            };

            if (grpcStatusCode != null)
            {
                message.TrailingHeaders.Add(GrpcProtocolConstants.StatusTrailer, grpcStatusCode.Value.ToString("D"));
            }

            return message;
        }

        private const int MessageDelimiterSize = 4; // how many bytes it takes to encode "Message-Length"
        private const int HeaderSize = MessageDelimiterSize + 1; // message length + compression flag

        public static Task WriteHeaderAsync(Stream stream, int length, bool compress, CancellationToken cancellationToken)
        {
            var headerData = new byte[HeaderSize];

            // Compression flag
            headerData[0] = compress ? (byte)1 : (byte)0;

            // Message length
            EncodeMessageLength(length, headerData.AsSpan(1));

            return stream.WriteAsync(headerData, 0, headerData.Length, cancellationToken);
        }

        private static void EncodeMessageLength(int messageLength, Span<byte> destination)
        {
            Debug.Assert(destination.Length >= MessageDelimiterSize, "Buffer too small to encode message length.");

            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)messageLength);
        }
    }
}
