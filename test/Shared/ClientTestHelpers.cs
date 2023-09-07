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
using System.Net.Http.Headers;
using Google.Protobuf;
using Greet;
using Grpc.Core;
using Grpc.Net.Compression;
using System;

namespace Grpc.Tests.Shared;

internal static class ClientTestHelpers
{
    public static readonly Marshaller<HelloRequest> HelloRequestMarshaller = Marshallers.Create<HelloRequest>(r => r.ToByteArray(), data => HelloRequest.Parser.ParseFrom(data));
    public static readonly Marshaller<HelloReply> HelloReplyMarshaller = Marshallers.Create<HelloReply>(r => r.ToByteArray(), data => HelloReply.Parser.ParseFrom(data));

    public static readonly Method<HelloRequest, HelloReply> ServiceMethod = GetServiceMethod(MethodType.Unary);

    public static Method<HelloRequest, HelloReply> GetServiceMethod(MethodType? methodType = null, Marshaller<HelloRequest>? requestMarshaller = null)
    {
        return new Method<HelloRequest, HelloReply>(methodType ?? MethodType.Unary, "ServiceName", "MethodName", requestMarshaller ?? HelloRequestMarshaller, HelloReplyMarshaller);
    }

    public static Method<TRequest, TResponse> GetServiceMethod<TRequest, TResponse>(MethodType methodType, Marshaller<TRequest> requestMarshaller, Marshaller<TResponse> responseMarshaller)
    {
        return new Method<TRequest, TResponse>(methodType, "ServiceName", "MethodName", requestMarshaller, responseMarshaller);
    }

    public static TestHttpMessageHandler CreateTestMessageHandler(HelloReply reply)
    {
        return TestHttpMessageHandler.Create(async r =>
        {
            var streamContent = await ClientTestHelpers.CreateResponseContent(reply).DefaultTimeout();
            return ResponseUtils.CreateResponse(HttpStatusCode.OK, streamContent);
        });
    }

    public static HttpClient CreateTestClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync, Uri? baseAddress = null)
    {
        var handler = TestHttpMessageHandler.Create(sendAsync);
        var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = baseAddress ?? new Uri("https://localhost");

        return httpClient;
    }

    public static HttpClient CreateTestClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync, Uri? baseAddress = null)
    {
        var handler = TestHttpMessageHandler.Create(sendAsync);
        var httpClient = new HttpClient(handler);
        httpClient.BaseAddress = baseAddress ?? new Uri("https://localhost");

        return httpClient;
    }

    public static Task<StreamContent> CreateResponseContent<TResponse>(TResponse response, ICompressionProvider? compressionProvider = null) where TResponse : IMessage<TResponse>
    {
        return CreateResponseContentCore(new[] { response }, compressionProvider);
    }

    public static Task<StreamContent> CreateResponsesContent<TResponse>(params TResponse[] responses) where TResponse : IMessage<TResponse>
    {
        return CreateResponseContentCore(responses, compressionProvider: null);
    }

    private static async Task<StreamContent> CreateResponseContentCore<TResponse>(TResponse[] responses, ICompressionProvider? compressionProvider) where TResponse : IMessage<TResponse>
    {
        var ms = new MemoryStream();
        foreach (var response in responses)
        {
            await WriteResponseAsync(ms, response, compressionProvider);
        }
        ms.Seek(0, SeekOrigin.Begin);
        var streamContent = new StreamContent(ms);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
        return streamContent;
    }

    public static async Task WriteResponseAsync<TResponse>(Stream ms, TResponse response, ICompressionProvider? compressionProvider) where TResponse : IMessage<TResponse>
    {
        var compress = false;

        byte[]? data;
        if (compressionProvider != null)
        {
            compress = true;

            var output = new MemoryStream();
            var compressionStream = compressionProvider.CreateCompressionStream(output, System.IO.Compression.CompressionLevel.Fastest);
            var compressedData = response.ToByteArray();

            compressionStream.Write(compressedData, 0, compressedData.Length);
            compressionStream.Flush();
            compressionStream.Dispose();
            data = output.ToArray();
        }
        else
        {
            data = response.ToByteArray();
        }

        await ResponseUtils.WriteHeaderAsync(ms, data.Length, compress, CancellationToken.None);
#if NET5_0_OR_GREATER
        await ms.WriteAsync(data);
#else
        await ms.WriteAsync(data, 0, data.Length);
#endif
    }

    public static async Task<byte[]> GetResponseDataAsync<TResponse>(TResponse response) where TResponse : IMessage<TResponse>
    {
        var ms = new MemoryStream();
        await WriteResponseAsync(ms, response, compressionProvider: null);
        return ms.ToArray();
    }
}
