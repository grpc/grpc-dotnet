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

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Compression;
using Grpc.AspNetCore.Server.Internal;
using Microsoft.AspNetCore.Http;

namespace Grpc.Tests.Shared
{
    internal static class MessageHelpers
    {
        private static readonly HttpContextServerCallContext TestServerCallContext = HttpContextServerCallContextHelper.CreateServerCallContext();

        public static T AssertReadMessage<T>(byte[] messageData, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : IMessage, new()
        {
            var ms = new MemoryStream(messageData);

            return AssertReadMessageAsync<T>(ms, compressionEncoding, compressionProviders).GetAwaiter().GetResult();
        }

        public static async Task<T> AssertReadMessageAsync<T>(Stream stream, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : IMessage, new()
        {
            compressionProviders = compressionProviders ?? new List<ICompressionProvider>
            {
                new GzipCompressionProvider(CompressionLevel.Fastest)
            };

            var pipeReader = new StreamPipeReader(stream);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageEncodingHeader] = compressionEncoding;

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext: httpContext,
                serviceOptions: new GrpcServiceOptions
                {
                    ResponseCompressionAlgorithm = compressionEncoding,
                    CompressionProviders = compressionProviders
                });

            var messageData = await pipeReader.ReadSingleMessageAsync(serverCallContext);

            var message = new T();
            message.MergeFrom(messageData);

            return message;
        }

        public static Task<T?> AssertReadStreamMessageAsync<T>(Stream stream, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage, new()
        {
            var pipeReader = new StreamPipeReader(stream);

            return AssertReadStreamMessageAsync<T>(pipeReader, compressionEncoding, compressionProviders);
        }

        public static async Task<T?> AssertReadStreamMessageAsync<T>(PipeReader pipeReader, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage, new()
        {
            compressionProviders = compressionProviders ?? new List<ICompressionProvider>
            {
                new GzipCompressionProvider(CompressionLevel.Fastest)
            };

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(serviceOptions: new GrpcServiceOptions
            {
                ResponseCompressionAlgorithm = compressionEncoding,
                CompressionProviders = compressionProviders
            });

            var messageData = await pipeReader.ReadStreamMessageAsync(serverCallContext);

            if (messageData == null)
            {
                return default;
            }

            var message = new T();
            message.MergeFrom(messageData);

            return message;
        }

        public static void WriteMessage<T>(Stream stream, T message, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : IMessage
        {
            compressionProviders = compressionProviders ?? new List<ICompressionProvider>
            {
                new GzipCompressionProvider(CompressionLevel.Fastest)
            };

            var messageData = message.ToByteArray();

            var pipeWriter = new StreamPipeWriter(stream);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = compressionEncoding;

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext: httpContext,
                serviceOptions: new GrpcServiceOptions
                {
                    ResponseCompressionAlgorithm = compressionEncoding,
                    CompressionProviders = compressionProviders
                });
            serverCallContext.Initialize();

            PipeExtensions.WriteMessageAsync(pipeWriter, messageData, serverCallContext, flush: true).GetAwaiter().GetResult();
			stream.Seek(0, SeekOrigin.Begin);
        }
    }
}
