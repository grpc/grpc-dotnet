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
using System.Threading;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.ClientFactory
{
    /// <summary>
    /// Interceptor that will set the current request's cancellation token and deadline onto CallOptions.
    /// This interceptor is registered with a singleton lifetime. The interceptor gets the request from
    /// IHttpContextAccessor, which is also a singleton. IHttpContextAccessor uses an async local value.
    /// </summary>
    internal class ContextPropagationInterceptor : Interceptor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ContextPropagationInterceptor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(ConfigureContext(context, out var cts));
            if (cts == null)
            {
                return call;
            }
            else
            {
                return new AsyncClientStreamingCall<TRequest, TResponse>(
                    call.RequestStream,
                    call.ResponseAsync,
                    call.ResponseHeadersAsync,
                    call.GetStatus,
                    call.GetTrailers,
                    () => { call.Dispose(); cts.Dispose(); });
            }
        }

        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(ConfigureContext(context, out var cts));
            if (cts == null)
            {
                return call;
            }
            else
            {
                return new AsyncDuplexStreamingCall<TRequest, TResponse>(
                    call.RequestStream,
                    call.ResponseStream,
                    call.ResponseHeadersAsync,
                    call.GetStatus,
                    call.GetTrailers,
                    () => { call.Dispose(); cts.Dispose(); });
            }
        }

        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, ConfigureContext(context, out var cts));
            if (cts == null)
            {
                return call;
            }
            else
            {
                return new AsyncServerStreamingCall<TResponse>(
                    call.ResponseStream,
                    call.ResponseHeadersAsync,
                    call.GetStatus,
                    call.GetTrailers,
                    () => { call.Dispose(); cts.Dispose(); });
            }
        }

        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var call = continuation(request, ConfigureContext(context, out var cts));
            if (cts == null)
            {
                return call;
            }
            else
            {
                return new AsyncUnaryCall<TResponse>(
                    call.ResponseAsync,
                    call.ResponseHeadersAsync,
                    call.GetStatus,
                    call.GetTrailers,
                    () => { call.Dispose(); cts.Dispose(); });
            }
        }

        public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
        {
            var response = continuation(request, ConfigureContext(context, out var cts));
            cts?.Dispose();
            return response;
        }

        private ClientInterceptorContext<TRequest, TResponse> ConfigureContext<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, out CancellationTokenSource? linkedCts)
            where TRequest : class
            where TResponse : class
        {
            linkedCts = null;

            var options = context.Options;
            var serverCallContext = GetServerCallContext();

            // Use propagated deadline if it is smaller than the specified deadline
            if (serverCallContext.Deadline < context.Options.Deadline.GetValueOrDefault(DateTime.MaxValue))
            {
                options = options.WithDeadline(serverCallContext.Deadline);
            }

            if (serverCallContext.CancellationToken.CanBeCanceled)
            {
                if (options.CancellationToken.CanBeCanceled)
                {
                    // If both propagated and options cancellation token can be canceled
                    // then set a new linked token of both
                    linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serverCallContext.CancellationToken, options.CancellationToken);
                    options = options.WithCancellationToken(linkedCts.Token);
                }
                else
                {
                    options = options.WithCancellationToken(serverCallContext.CancellationToken);
                }
            }

            return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
        }

        private ServerCallContext GetServerCallContext()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                throw new InvalidOperationException("Unable to propagate server context values to the call. Can't find the current HttpContext.");
            }

            var serverCallContext = httpContext.Features.Get<IServerCallContextFeature>()?.ServerCallContext;
            if (serverCallContext == null)
            {
                throw new InvalidOperationException("Unable to propagate server context values to the call. Can't find the current gRPC ServerCallContext.");
            }

            return serverCallContext;
        }
    }
}
