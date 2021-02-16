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
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.Net.Client.Internal
{
    /// <summary>
    /// A client-side RPC invocation using HttpClient.
    /// </summary>
    internal sealed class HttpClientCallInvoker : CallInvoker
    {
        internal GrpcChannel Channel { get; }

        public HttpClientCallInvoker(GrpcChannel channel)
        {
            Channel = channel;
        }

        /// <summary>
        /// Invokes a client streaming call asynchronously.
        /// In client streaming scenario, client sends a stream of requests and server responds with a single response.
        /// </summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options);
            call.StartClientStreaming();

            return new AsyncClientStreamingCall<TRequest, TResponse>(
                requestStream: call.ClientStreamWriter,
                responseAsync: call.GetResponseAsync(),
                responseHeadersAsync: Callbacks<TRequest, TResponse>.GetResponseHeadersAsync,
                getStatusFunc: Callbacks<TRequest, TResponse>.GetStatus,
                getTrailersFunc: Callbacks<TRequest, TResponse>.GetTrailers,
                disposeAction: Callbacks<TRequest, TResponse>.Dispose,
                call);
        }

        /// <summary>
        /// Invokes a duplex streaming call asynchronously.
        /// In duplex streaming scenario, client sends a stream of requests and server responds with a stream of responses.
        /// The response stream is completely independent and both side can be sending messages at the same time.
        /// </summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options);
            call.StartDuplexStreaming();

            return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                requestStream: call.ClientStreamWriter,
                responseStream: call.ClientStreamReader,
                responseHeadersAsync: Callbacks<TRequest, TResponse>.GetResponseHeadersAsync,
                getStatusFunc: Callbacks<TRequest, TResponse>.GetStatus,
                getTrailersFunc: Callbacks<TRequest, TResponse>.GetTrailers,
                disposeAction: Callbacks<TRequest, TResponse>.Dispose,
                call);
        }

        /// <summary>
        /// Invokes a server streaming call asynchronously.
        /// In server streaming scenario, client sends on request and server responds with a stream of responses.
        /// </summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options);
            call.StartServerStreaming(request);

            return new AsyncServerStreamingCall<TResponse>(
                responseStream: call.ClientStreamReader,
                responseHeadersAsync: Callbacks<TRequest, TResponse>.GetResponseHeadersAsync,
                getStatusFunc: Callbacks<TRequest, TResponse>.GetStatus,
                getTrailersFunc: Callbacks<TRequest, TResponse>.GetTrailers,
                disposeAction: Callbacks<TRequest, TResponse>.Dispose,
                call);
        }

        /// <summary>
        /// Invokes a simple remote call asynchronously.
        /// </summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            var call = CreateGrpcCall<TRequest, TResponse>(method, options);
            call.StartUnary(request);

            return new AsyncUnaryCall<TResponse>(
                responseAsync: call.GetResponseAsync(),
                responseHeadersAsync: Callbacks<TRequest, TResponse>.GetResponseHeadersAsync,
                getStatusFunc: Callbacks<TRequest, TResponse>.GetStatus,
                getTrailersFunc: Callbacks<TRequest, TResponse>.GetTrailers,
                disposeAction: Callbacks<TRequest, TResponse>.Dispose,
                call);
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
            CallOptions options)
            where TRequest : class
            where TResponse : class
        {
            if (Channel.Disposed)
            {
                throw new ObjectDisposedException(nameof(GrpcChannel));
            }

            var methodInfo = Channel.GetCachedGrpcMethodInfo(method);
            var call = new GrpcCall<TRequest, TResponse>(method, methodInfo, options, Channel);

            return call;
        }

        // Store static callbacks so delegates are allocated once
        private static class Callbacks<TRequest, TResponse>
            where TRequest : class
            where TResponse : class
        {
            internal static readonly Func<object, Task<Metadata>> GetResponseHeadersAsync = state => ((GrpcCall<TRequest, TResponse>)state).GetResponseHeadersAsync();
            internal static readonly Func<object, Status> GetStatus = state => ((GrpcCall<TRequest, TResponse>)state).GetStatus();
            internal static readonly Func<object, Metadata> GetTrailers = state => ((GrpcCall<TRequest, TResponse>)state).GetTrailers();
            internal static readonly Action<object> Dispose = state => ((GrpcCall<TRequest, TResponse>)state).Dispose();
        }
    }
}
