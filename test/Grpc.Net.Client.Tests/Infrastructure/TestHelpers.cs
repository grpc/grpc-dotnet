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
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Greet;
using Grpc.Core;

namespace Grpc.Net.Client.Tests.Infrastructure
{
    public static class TestHelpers
    {
        public static readonly Marshaller<HelloRequest> HelloRequestMarshaller = Marshallers.Create<HelloRequest>(r => r.ToByteArray(), data => HelloRequest.Parser.ParseFrom(data));
        public static readonly Marshaller<HelloReply> HelloReplyMarshaller = Marshallers.Create<HelloReply>(r => r.ToByteArray(), data => HelloReply.Parser.ParseFrom(data));

        public static readonly Method<HelloRequest, HelloReply> ServiceMethod = new Method<HelloRequest, HelloReply>(MethodType.Unary, "ServiceName", "MethodName", HelloRequestMarshaller, HelloReplyMarshaller);

        public static HttpClient CreateTestClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> sendAsync)
        {
            var handler = TestHttpMessageHandler.Create(sendAsync);
            var httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri("https://localhost");

            return httpClient;
        }

        public static HttpClient CreateTestClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        {
            var handler = TestHttpMessageHandler.Create(sendAsync);
            var httpClient = new HttpClient(handler);
            httpClient.BaseAddress = new Uri("https://localhost");

            return httpClient;
        }

        public static async Task<StreamContent> CreateResponseContent<TResponse>(params TResponse[] responses) where TResponse : IMessage<TResponse>
        {
            var ms = new MemoryStream();
            foreach (var response in responses)
            {
                await WriteResponseAsync(ms, response);
            }
            ms.Seek(0, SeekOrigin.Begin);
            var streamContent = new StreamContent(ms);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            return streamContent;
        }

        public static async Task WriteResponseAsync<TResponse>(Stream ms, TResponse response) where TResponse : IMessage<TResponse>
        {
            await ResponseUtils.WriteHeaderAsync(ms, response.CalculateSize(), false, CancellationToken.None);
            await ms.WriteAsync(response.ToByteArray());
        }

        public static async Task<byte[]> GetResponseDataAsync<TResponse>(TResponse response) where TResponse : IMessage<TResponse>
        {
            var ms = new MemoryStream();
            await WriteResponseAsync(ms, response);
            return ms.ToArray();
        }
    }
}
