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
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.NetCore.HttpClient
{
    /// <summary>
    /// A client-side RPC invocation using HttpClient.
    /// </summary>
    public class HttpClientCallInvoker : CallInvoker
    {
        private System.Net.Http.HttpClient _client;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientCallInvoker"/> class.
        /// </summary>
        /// <param name="handler">The primary client handler to use for gRPC requests.</param>
        /// <param name="baseAddress">The base address to use when making gRPC requests.</param>
        public HttpClientCallInvoker(HttpClientHandler handler, Uri baseAddress)
        {
            _client = new System.Net.Http.HttpClient(handler);
            _client.BaseAddress = baseAddress;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientCallInvoker"/> class.
        /// </summary>
        /// <param name="client">The HttpClient to use for gRPC requests.</param>
        public HttpClientCallInvoker(System.Net.Http.HttpClient client)
        {
            _client = client;
        }

        internal Uri BaseAddress => _client.BaseAddress;

        /// <summary>
        /// Token that can be used for cancelling the call on the client side.
        /// Cancelling the token will request cancellation
        /// of the remote call. Best effort will be made to deliver the cancellation
        /// notification to the server and interaction of the call with the server side
        /// will be terminated. Unless the call finishes before the cancellation could
        /// happen (there is an inherent race),
        /// the call will finish with <c>StatusCode.Cancelled</c> status.
        /// </summary>
        public CancellationToken CancellationToken { get; set; }

        /// <summary>
        /// The call deadline.
        /// </summary>
        public DateTime Deadline { get; set; }

        /// <summary>
        /// Token for propagating parent call context.
        /// </summary>
        public ContextPropagationToken PropagationToken { get; set; }

        /// <summary>
        /// Invokes a client streaming call asynchronously.
        /// In client streaming scenario, client sends a stream of requests and server responds with a single response.
        /// </summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            var pipeContent = new PipeContent();
            var message = new HttpRequestMessage(HttpMethod.Post, method.FullName);
            message.Content = pipeContent;
            message.Version = new Version(2, 0);

            var sendTask = SendRequestMessageAsync<TRequest>(() => Task.CompletedTask, _client, message);

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                requestStream: new PipeClientStreamWriter<TRequest>(pipeContent.PipeWriter, method.RequestMarshaller.Serializer, options.WriteOptions),
                responseAsync: GetResponseAsync(sendTask, method.ResponseMarshaller.Deserializer),
                responseHeadersAsync: GetResponseHeadersAsync(sendTask),
                // Cannot implement due to trailers being unimplemented
                getStatusFunc: () => new Status(),
                // Cannot implement due to trailers being unimplemented
                getTrailersFunc: () => new Metadata(),
                disposeAction: () => { });
        }

        /// <summary>
        /// Invokes a duplex streaming call asynchronously.
        /// In duplex streaming scenario, client sends a stream of requests and server responds with a stream of responses.
        /// The response stream is completely independent and both side can be sending messages at the same time.
        /// </summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            var pipeContent = new PipeContent();
            var message = new HttpRequestMessage(HttpMethod.Post, method.FullName);
            message.Content = pipeContent;
            message.Version = new Version(2, 0);

            var sendTask = SendRequestMessageAsync<TRequest>(() => Task.CompletedTask, _client, message);

            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                requestStream: new PipeClientStreamWriter<TRequest>(pipeContent.PipeWriter, method.RequestMarshaller.Serializer, options.WriteOptions),
                responseStream: new ClientAsyncStreamReader<TResponse>(sendTask, method.ResponseMarshaller.Deserializer),
                responseHeadersAsync: GetResponseHeadersAsync(sendTask),
                // Cannot implement due to trailers being unimplemented
                getStatusFunc: () => new Status(),
                // Cannot implement due to trailers being unimplemented
                getTrailersFunc: () => new Metadata(),
                disposeAction: () => { });
        }

        /// <summary>
        /// Invokes a server streaming call asynchronously.
        /// In server streaming scenario, client sends on request and server responds with a stream of responses.
        /// </summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var content = new PipeContent();
            var message = new HttpRequestMessage(HttpMethod.Post, method.FullName);
            message.Content = content;
            message.Version = new Version(2, 0);

            // Write request body
            var sendTask = SendRequestMessageAsync<TRequest>(
                async () =>
                {
                    await content.PipeWriter.WriteMessageCoreAsync(method.RequestMarshaller.Serializer(request), true);
                    content.PipeWriter.Complete();
                },
                _client, message);

            return new AsyncServerStreamingCall<TResponse>(
                responseStream: new ClientAsyncStreamReader<TResponse>(sendTask, method.ResponseMarshaller.Deserializer),
                responseHeadersAsync: GetResponseHeadersAsync(sendTask),
                // Cannot implement due to trailers being unimplemented
                getStatusFunc: () => new Status(),
                // Cannot implement due to trailers being unimplemented
                getTrailersFunc: () => new Metadata(),
                disposeAction: () => { });
        }

        /// <summary>
        /// Invokes a simple remote call asynchronously.
        /// </summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var content = new PipeContent();
            var message = new HttpRequestMessage(HttpMethod.Post, method.FullName);
            message.Content = content;
            message.Version = new Version(2, 0);

            // Write request body
            var sendTask = SendRequestMessageAsync<TRequest>(
                async () =>
                {
                    await content.PipeWriter.WriteMessageCoreAsync(method.RequestMarshaller.Serializer(request), true);
                    content.PipeWriter.Complete();
                },
                _client, message);

            return new AsyncUnaryCall<TResponse>(
                responseAsync: GetResponseAsync(sendTask, method.ResponseMarshaller.Deserializer),
                responseHeadersAsync: GetResponseHeadersAsync(sendTask),
                // Cannot implement due to trailers being unimplemented
                getStatusFunc: () => new Status(),
                // Cannot implement due to trailers being unimplemented
                getTrailersFunc: () => new Metadata(),
                disposeAction: () => { });
        }

        /// <summary>
        /// Invokes a simple remote call in a blocking fashion.
        /// </summary>
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return AsyncUnaryCall(method, host, options, request)?.GetAwaiter().GetResult();
        }

        private static async Task<HttpResponseMessage> SendRequestMessageAsync<TRequest>(Func<Task> writeMessageTask, System.Net.Http.HttpClient client, HttpRequestMessage message)
        {
            await writeMessageTask();
            return await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead);
        }

        private static async Task<TResponse> GetResponseAsync<TResponse>(Task<HttpResponseMessage> sendTask, Func<byte[], TResponse> deserializer)
        {
            // We can't use pipes here since we can't control how much is read and response trailers causes InvalidOperationException
            var response = await sendTask;
            var responseStream = await response.Content.ReadAsStreamAsync();

            return responseStream.ReadSingleMessage(deserializer);
        }

        private static async Task<Metadata> GetResponseHeadersAsync(Task<HttpResponseMessage> sendTask)
        {
            var response = await sendTask;

            var headers = new Metadata();

            foreach (var header in response.Headers)
            {
                // ASP.NET Core includes pseudo headers in the set of request headers
                // whereas, they are not in gRPC implementations. We will filter them
                // out when we construct the list of headers on the context.
                if (header.Key.StartsWith(":", StringComparison.Ordinal))
                {
                    continue;
                }
                else if (header.Key.EndsWith(Metadata.BinaryHeaderSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    headers.Add(header.Key, ParseBinaryHeader(string.Join(",", header.Value)));
                }
                else
                {
                    headers.Add(header.Key, string.Join(",", header.Value));
                }
            }
            return null;
        }

        private static byte[] ParseBinaryHeader(string base64)
        {
            string decodable;
            switch (base64.Length % 4)
            {
                case 0:
                    // base64 has the required padding 
                    decodable = base64;
                    break;
                case 2:
                    // 2 chars padding
                    decodable = base64 + "==";
                    break;
                case 3:
                    // 3 chars padding
                    decodable = base64 + "=";
                    break;
                default:
                    // length%4 == 1 should be illegal
                    throw new FormatException("Invalid base64 header value");
            }

            return Convert.FromBase64String(decodable);
        }
    }
}
