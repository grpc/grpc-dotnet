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
using Grpc.Core;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Grpc.Net.Client
{
    /// <summary>
    /// A client-side RPC invocation using HttpClient.
    /// </summary>
    public sealed class HttpClientCallInvoker : CallInvoker
    {
        private readonly HttpClient _client;
        private readonly ILoggerFactory _loggerFactory;

        // Override the current time for unit testing
        internal ISystemClock Clock = SystemClock.Instance;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientCallInvoker"/> class.
        /// </summary>
        /// <param name="client">The HttpClient to use for gRPC requests.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
        public HttpClientCallInvoker(HttpClient client, ILoggerFactory? loggerFactory)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            _client = client;
            _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            Deadline = DateTime.MaxValue;
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
        /// Invokes a client streaming call asynchronously.
        /// In client streaming scenario, client sends a stream of requests and server responds with a single response.
        /// </summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options, out var disposeAction);
            call.StartClientStreaming(_client);

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                requestStream: call.ClientStreamWriter,
                responseAsync: call.GetResponseAsync(),
                responseHeadersAsync: call.GetResponseHeadersAsync(),
                getStatusFunc: call.GetStatus,
                getTrailersFunc: call.GetTrailers,
                disposeAction: disposeAction);
        }

        /// <summary>
        /// Invokes a duplex streaming call asynchronously.
        /// In duplex streaming scenario, client sends a stream of requests and server responds with a stream of responses.
        /// The response stream is completely independent and both side can be sending messages at the same time.
        /// </summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options, out var disposeAction);
            call.StartDuplexStreaming(_client);

            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                requestStream: call.ClientStreamWriter,
                responseStream: call.ClientStreamReader,
                responseHeadersAsync: call.GetResponseHeadersAsync(),
                getStatusFunc: call.GetStatus,
                getTrailersFunc: call.GetTrailers,
                disposeAction: disposeAction);
        }

        /// <summary>
        /// Invokes a server streaming call asynchronously.
        /// In server streaming scenario, client sends on request and server responds with a stream of responses.
        /// </summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options, out var disposeAction);
            call.StartServerStreaming(_client, request);

            return new AsyncServerStreamingCall<TResponse>(
                responseStream: call.ClientStreamReader,
                responseHeadersAsync: call.GetResponseHeadersAsync(),
                getStatusFunc: call.GetStatus,
                getTrailersFunc: call.GetTrailers,
                disposeAction: disposeAction);
        }

        /// <summary>
        /// Invokes a simple remote call asynchronously.
        /// </summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options, out var disposeAction);
            call.StartUnary(_client, request);

            return new AsyncUnaryCall<TResponse>(
                responseAsync: call.GetResponseAsync(),
                responseHeadersAsync: call.GetResponseHeadersAsync(),
                getStatusFunc: call.GetStatus,
                getTrailersFunc: call.GetTrailers,
                disposeAction: disposeAction);
        }

        /// <summary>
        /// Invokes a simple remote call in a blocking fashion.
        /// </summary>
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var call = AsyncUnaryCall(method, host, options, request);
            return call.ResponseAsync.GetAwaiter().GetResult();
        }

        private GrpcCall<TRequest, TResponse> CreateGrpcCall<TRequest, TResponse>(
            Method<TRequest, TResponse> method,
            CallOptions options,
            out Action disposeAction)
            where TRequest : class
            where TResponse : class
        {
            CancellationTokenSource? linkedCts = null;

            // Use propagated deadline if it is small than the specified deadline
            if (Deadline < options.Deadline)
            {
                options = options.WithDeadline(Deadline);
            }

            if (CancellationToken.CanBeCanceled)
            {
                if (options.CancellationToken.CanBeCanceled)
                {
                    // If both propagated and options cancellation token can be canceled
                    // then set a new linked token of both
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, options.CancellationToken);
                    options = options.WithCancellationToken(linkedCts.Token);
                }
                else
                {
                    options = options.WithCancellationToken(CancellationToken);
                }
            }

            var call = new GrpcCall<TRequest, TResponse>(method, options, Clock, _loggerFactory);

            // Clean up linked cancellation token
            disposeAction = linkedCts != null
                ? () => { call.Dispose(); linkedCts!.Dispose(); }
                : (Action)call.Dispose;

            return call;
        }
    }
}
