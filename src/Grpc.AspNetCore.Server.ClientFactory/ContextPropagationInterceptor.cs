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

using System.Diagnostics.CodeAnalysis;
using Grpc.AspNetCore.Server;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.ClientFactory
{
    /// <summary>
    /// Interceptor that will set the current request's cancellation token and deadline onto CallOptions.
    /// This interceptor is registered with a singleton lifetime. The interceptor gets the request from
    /// IHttpContextAccessor, which is also a singleton. IHttpContextAccessor uses an async local value.
    /// </summary>
    internal class ContextPropagationInterceptor : Interceptor
    {
        private readonly GrpcContextPropagationOptions _options;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger _logger;

        public ContextPropagationInterceptor(IOptions<GrpcContextPropagationOptions> options, IHttpContextAccessor httpContextAccessor, ILogger<ContextPropagationInterceptor> logger)
        {
            _options = options.Value;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
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
                    requestStream: call.RequestStream,
                    responseAsync: call.ResponseAsync,
                    responseHeadersAsync: ClientStreamingCallbacks<TRequest, TResponse>.GetResponseHeadersAsync,
                    getStatusFunc: ClientStreamingCallbacks<TRequest, TResponse>.GetStatus,
                    getTrailersFunc: ClientStreamingCallbacks<TRequest, TResponse>.GetTrailers,
                    disposeAction: ClientStreamingCallbacks<TRequest, TResponse>.Dispose,
                    CreateContextState(call, cts));
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
                    requestStream: call.RequestStream,
                    responseStream: call.ResponseStream,
                    responseHeadersAsync: DuplexStreamingCallbacks<TRequest, TResponse>.GetResponseHeadersAsync,
                    getStatusFunc: DuplexStreamingCallbacks<TRequest, TResponse>.GetStatus,
                    getTrailersFunc: DuplexStreamingCallbacks<TRequest, TResponse>.GetTrailers,
                    disposeAction: DuplexStreamingCallbacks<TRequest, TResponse>.Dispose,
                    CreateContextState(call, cts));
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
                    responseStream: call.ResponseStream,
                    responseHeadersAsync: ServerStreamingCallbacks<TResponse>.GetResponseHeadersAsync,
                    getStatusFunc: ServerStreamingCallbacks<TResponse>.GetStatus,
                    getTrailersFunc: ServerStreamingCallbacks<TResponse>.GetTrailers,
                    disposeAction: ServerStreamingCallbacks<TResponse>.Dispose,
                    CreateContextState(call, cts));
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
                    responseAsync: call.ResponseAsync,
                    responseHeadersAsync: UnaryCallbacks<TResponse>.GetResponseHeadersAsync,
                    getStatusFunc: UnaryCallbacks<TResponse>.GetStatus,
                    getTrailersFunc: UnaryCallbacks<TResponse>.GetTrailers,
                    disposeAction: UnaryCallbacks<TResponse>.Dispose,
                    CreateContextState(call, cts));
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
            if (TryGetServerCallContext(out var serverCallContext, out var errorMessage))
            {
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
            }
            else
            {
                Log.PropagateServerCallContextFailure(_logger, errorMessage);

                if (!_options.SuppressContextNotFoundErrors)
                {
                    throw new InvalidOperationException("Unable to propagate server context values to the call. " + errorMessage);
                }
            }

            return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, options);
        }

        private bool TryGetServerCallContext([NotNullWhen(true)] out ServerCallContext? serverCallContext, [NotNullWhen(false)] out string? errorMessage)
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                errorMessage = "Can't find the current HttpContext.";
                serverCallContext = null;
                return false;
            }

            serverCallContext = httpContext.Features.Get<IServerCallContextFeature>()?.ServerCallContext;
            if (serverCallContext == null)
            {
                errorMessage = "Can't find the gRPC ServerCallContext on the current HttpContext.";
                serverCallContext = null;
                return false;
            }

            errorMessage = null;
            return true;
        }

        private ContextState<TCall> CreateContextState<TCall>(TCall call, CancellationTokenSource cancellationTokenSource) where TCall : IDisposable =>
            new ContextState<TCall>(call, cancellationTokenSource);

        private class ContextState<TCall> : IDisposable where TCall : IDisposable
        {
            public ContextState(TCall call, CancellationTokenSource cancellationTokenSource)
            {
                Call = call;
                CancellationTokenSource = cancellationTokenSource;
            }

            public TCall Call { get; }
            public CancellationTokenSource CancellationTokenSource { get; }

            public void Dispose()
            {
                Call.Dispose();
                CancellationTokenSource.Dispose();
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _propagateServerCallContextFailure =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "PropagateServerCallContextFailure"), "Unable to propagate server context values to the call. {ErrorMessage}");

            public static void PropagateServerCallContextFailure(ILogger logger, string errorMessage)
            {
                _propagateServerCallContextFailure(logger, errorMessage, null);
            }
        }

        // Store static callbacks so delegates are allocated once
        private static class UnaryCallbacks<TResponse>
            where TResponse : class
        {
            internal static readonly Func<object, Task<Metadata>> GetResponseHeadersAsync = state => ((ContextState<AsyncUnaryCall<TResponse>>)state).Call.ResponseHeadersAsync;
            internal static readonly Func<object, Status> GetStatus = state => ((ContextState<AsyncUnaryCall<TResponse>>)state).Call.GetStatus();
            internal static readonly Func<object, Metadata> GetTrailers = state => ((ContextState<AsyncUnaryCall<TResponse>>)state).Call.GetTrailers();
            internal static readonly Action<object> Dispose = state => ((ContextState<AsyncUnaryCall<TResponse>>)state).Dispose();
        }

        private static class ServerStreamingCallbacks<TResponse>
            where TResponse : class
        {
            internal static readonly Func<object, Task<Metadata>> GetResponseHeadersAsync = state => ((ContextState<AsyncServerStreamingCall<TResponse>>)state).Call.ResponseHeadersAsync;
            internal static readonly Func<object, Status> GetStatus = state => ((ContextState<AsyncServerStreamingCall<TResponse>>)state).Call.GetStatus();
            internal static readonly Func<object, Metadata> GetTrailers = state => ((ContextState<AsyncServerStreamingCall<TResponse>>)state).Call.GetTrailers();
            internal static readonly Action<object> Dispose = state => ((ContextState<AsyncServerStreamingCall<TResponse>>)state).Dispose();
        }

        private static class DuplexStreamingCallbacks<TRequest, TResponse>
            where TRequest : class
            where TResponse : class
        {
            internal static readonly Func<object, Task<Metadata>> GetResponseHeadersAsync = state => ((ContextState<AsyncDuplexStreamingCall<TRequest, TResponse>>)state).Call.ResponseHeadersAsync;
            internal static readonly Func<object, Status> GetStatus = state => ((ContextState<AsyncDuplexStreamingCall<TRequest, TResponse>>)state).Call.GetStatus();
            internal static readonly Func<object, Metadata> GetTrailers = state => ((ContextState<AsyncDuplexStreamingCall<TRequest, TResponse>>)state).Call.GetTrailers();
            internal static readonly Action<object> Dispose = state => ((ContextState<AsyncDuplexStreamingCall<TRequest, TResponse>>)state).Dispose();
        }

        private static class ClientStreamingCallbacks<TRequest, TResponse>
            where TRequest : class
            where TResponse : class
        {
            internal static readonly Func<object, Task<Metadata>> GetResponseHeadersAsync = state => ((ContextState<AsyncClientStreamingCall<TRequest, TResponse>>)state).Call.ResponseHeadersAsync;
            internal static readonly Func<object, Status> GetStatus = state => ((ContextState<AsyncClientStreamingCall<TRequest, TResponse>>)state).Call.GetStatus();
            internal static readonly Func<object, Metadata> GetTrailers = state => ((ContextState<AsyncClientStreamingCall<TRequest, TResponse>>)state).Call.GetTrailers();
            internal static readonly Action<object> Dispose = state => ((ContextState<AsyncClientStreamingCall<TRequest, TResponse>>)state).Dispose();
        }
    }
}
