﻿#region Copyright notice and license

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
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;
using Grpc.AspNetCore.Server;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Grpc.Net.Compression;
using Microsoft.AspNetCore.Http;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Grpc.Tests.Shared
{
    internal static class MessageHelpers
    {
        public static Marshaller<TMessage> GetMarshaller<TMessage>(MessageParser<TMessage> parser) where TMessage : IMessage<TMessage> =>
            Marshallers.Create<TMessage>(r => r.ToByteArray(), data => parser.ParseFrom(data));

        public static readonly Method<HelloRequest, HelloReply> ServiceMethod = CreateServiceMethod("MethodName", HelloRequest.Parser, HelloReply.Parser);

        public static Method<TRequest, TResponse> CreateServiceMethod<TRequest, TResponse>(string methodName, MessageParser<TRequest> requestParser, MessageParser<TResponse> responseParser)
             where TRequest : IMessage<TRequest>
             where TResponse : IMessage<TResponse>
        {
            return new Method<TRequest, TResponse>(MethodType.Unary, "ServiceName", methodName, GetMarshaller(requestParser), GetMarshaller(responseParser));
        }

        private static readonly HttpContextServerCallContext TestServerCallContext = HttpContextServerCallContextHelper.CreateServerCallContext();

        public static T AssertReadMessage<T>(byte[] messageData, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage, new()
        {
            var ms = new MemoryStream(messageData);

            return AssertReadMessageAsync<T>(ms, compressionEncoding, compressionProviders).GetAwaiter().GetResult();
        }

        public static async Task<T> AssertReadMessageAsync<T>(Stream stream, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage, new()
        {
            compressionProviders = compressionProviders ?? new List<ICompressionProvider>
            {
                new GzipCompressionProvider(CompressionLevel.Fastest)
            };

            var pipeReader = PipeReader.Create(stream);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageEncodingHeader] = compressionEncoding;

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext: httpContext,
                compressionProviders: compressionProviders,
                responseCompressionAlgorithm: compressionEncoding);

            var message = await pipeReader.ReadSingleMessageAsync<T>(serverCallContext, Deserialize<T>).AsTask().DefaultTimeout();

            return message;
        }

        public static Task<T?> AssertReadStreamMessageAsync<T>(Stream stream, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage, new()
        {
            var pipeReader = PipeReader.Create(stream);

            return AssertReadStreamMessageAsync<T>(pipeReader, compressionEncoding, compressionProviders);
        }

        public static async Task<T?> AssertReadStreamMessageAsync<T>(PipeReader pipeReader, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage, new()
        {
            compressionProviders = compressionProviders ?? new List<ICompressionProvider>
            {
                new GzipCompressionProvider(CompressionLevel.Fastest)
            };

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageEncodingHeader] = compressionEncoding;

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext: httpContext,
                compressionProviders: compressionProviders,
                responseCompressionAlgorithm: compressionEncoding);

            var message = await pipeReader.ReadStreamMessageAsync<T>(serverCallContext, Deserialize<T>).AsTask().DefaultTimeout();

            return message;
        }

        public static void WriteMessage<T>(Stream stream, T message, string? compressionEncoding = null, List<ICompressionProvider>? compressionProviders = null) where T : class, IMessage
        {
            compressionProviders = compressionProviders ?? new List<ICompressionProvider>
            {
                new GzipCompressionProvider(CompressionLevel.Fastest)
            };

            var pipeWriter = PipeWriter.Create(stream);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers[GrpcProtocolConstants.MessageAcceptEncodingHeader] = compressionEncoding;

            var serverCallContext = HttpContextServerCallContextHelper.CreateServerCallContext(
                httpContext: httpContext,
                compressionProviders: compressionProviders,
                responseCompressionAlgorithm: compressionEncoding);
            serverCallContext.Initialize();

            pipeWriter.WriteMessageAsync(message, serverCallContext, (r, c) => c.Complete(r.ToByteArray()), canFlush: true).GetAwaiter().GetResult();
            stream.Seek(0, SeekOrigin.Begin);
        }

        private static Dictionary<string, ICompressionProvider> ResolveProviders(List<ICompressionProvider> compressionProviders)
        {
            var resolvedProviders = new Dictionary<string, ICompressionProvider>(StringComparer.Ordinal);
            foreach (var compressionProvider in compressionProviders)
            {
                if (!resolvedProviders.ContainsKey(compressionProvider.EncodingName))
                {
                    resolvedProviders.Add(compressionProvider.EncodingName, compressionProvider);
                }
            }

            return resolvedProviders;
        }

        private static T Deserialize<T>(DeserializationContext context) where T : class, IMessage, new()
        {
            var data = context.PayloadAsNewBuffer();

            if (data == null)
            {
                return null!;
            }

            var message = new T();
            message.MergeFrom(data);
            return message;
        }
    }
}
