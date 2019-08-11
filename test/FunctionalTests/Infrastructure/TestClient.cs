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

using System.Net.Http;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.Client.Internal;
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.FunctionalTests.Infrastructure
{
    internal class TestClient<TRequest, TResponse> : ClientBase
        where TRequest : class
        where TResponse : class
    {
        private readonly HttpClientCallInvoker _callInvoker;
        private readonly Method<TRequest, TResponse> _method;

        public TestClient(HttpClient httpClient, ILoggerFactory loggerFactory, Method<TRequest, TResponse> method, bool disableClientDeadlineTimer = false)
        {
            var channel = GrpcChannel.ForAddress(httpClient.BaseAddress, new GrpcChannelOptions
            {
                LoggerFactory = loggerFactory,
                HttpClient = httpClient
            });
            channel.DisableClientDeadlineTimer = disableClientDeadlineTimer;

            _callInvoker = new HttpClientCallInvoker(channel);
            _method = method;
        }

        public AsyncUnaryCall<TResponse> UnaryCall(TRequest request, CallOptions? callOptions = null)
        {
            return _callInvoker.AsyncUnaryCall<TRequest, TResponse>(_method, string.Empty, callOptions ?? new CallOptions(), request);
        }

        public AsyncClientStreamingCall<TRequest, TResponse> ClientStreamingCall(CallOptions? callOptions = null)
        {
            return _callInvoker.AsyncClientStreamingCall<TRequest, TResponse>(_method, string.Empty, callOptions ?? new CallOptions());
        }

        public AsyncServerStreamingCall<TResponse> ServerStreamingCall(TRequest request, CallOptions? callOptions = null)
        {
            return _callInvoker.AsyncServerStreamingCall<TRequest, TResponse>(_method, string.Empty, callOptions ?? new CallOptions(), request);
        }

        public AsyncDuplexStreamingCall<TRequest, TResponse> DuplexStreamingCall(CallOptions? callOptions = null)
        {
            return _callInvoker.AsyncDuplexStreamingCall<TRequest, TResponse>(_method, string.Empty, callOptions ?? new CallOptions());
        }
    }

    internal static class TestClientFactory
    {
        public static TestClient<TRequest, TResponse> Create<TRequest, TResponse>(
            HttpClient httpClient,
            ILoggerFactory loggerFactory,
            Method<TRequest, TResponse> method,
            bool disableClientDeadlineTimer = false)
            where TRequest : class
            where TResponse : class
        {
            return new TestClient<TRequest, TResponse>(httpClient, loggerFactory, method, disableClientDeadlineTimer);
        }
    }
}
