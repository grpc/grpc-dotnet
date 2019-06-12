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
using System.Linq;
using System.Net.Http;
using System.Threading;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Grpc.Core.Utils;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.Net.ClientFactory.Internal
{
    // Note that the constraint is set to class to allow clients inheriting from ClientBase and LiteClientBase
    internal class GrpcHttpClientFactory<TClient> : INamedTypedHttpClientFactory<TClient> where TClient : class
    {
        private readonly Cache _cache;
        private readonly IServiceProvider _services;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptionsMonitor<GrpcClientFactoryOptions> _optionsMonitor;

        public GrpcHttpClientFactory(
            Cache cache,
            IServiceProvider services,
            ILoggerFactory loggerFactory,
            IOptionsMonitor<GrpcClientFactoryOptions> optionsMonitor)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            if (optionsMonitor == null)
            {
                throw new ArgumentNullException(nameof(optionsMonitor));
            }

            _cache = cache;
            _services = services;
            _loggerFactory = loggerFactory;
            _optionsMonitor = optionsMonitor;
        }

        public TClient CreateClient(HttpClient httpClient, string name)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            var httpClientCallInvoker = new HttpClientCallInvoker(httpClient, _loggerFactory);

            var options = _optionsMonitor.Get(name);
            for (var i = 0; i < options.CallInvokerActions.Count; i++)
            {
                options.CallInvokerActions[i](httpClientCallInvoker);
            }

            return (TClient)_cache.Activator(_services, new object[] { httpClientCallInvoker.Intercept(options.Interceptors.ToArray()) });
        }

        // The Cache should be registered as a singleton, so it that it can
        // act as a cache for the Activator. This allows the outer class to be registered
        // as a transient, so that it doesn't close over the application root service provider.
        public class Cache
        {
            private readonly static Func<ObjectFactory> _createActivator = () => ActivatorUtilities.CreateFactory(typeof(TClient), new Type[] { typeof(CallInvoker), });

            private ObjectFactory? _activator;
            private bool _initialized;
            private object? _lock;

            public ObjectFactory Activator
            {
                get
                {
                    var activator = LazyInitializer.EnsureInitialized(
                        ref _activator,
                        ref _initialized,
                        ref _lock,
                        _createActivator);

                    // TODO(JamesNK): Compiler thinks activator is nullable
                    // Possibly remove in the future when compiler is fixed
                    return activator!;
                }
            }
        }
    }


    /// <summary>
    /// Extends the CallInvoker class to provide the interceptor facility on the client side.
    /// </summary>
    static class CallInvokerExtensions
    {
        /// <summary>
        /// Returns a <see cref="Grpc.Core.CallInvoker" /> instance that intercepts
        /// the invoker with the given interceptor.
        /// </summary>
        /// <param name="invoker">The underlying invoker to intercept.</param>
        /// <param name="interceptor">The interceptor to intercept calls to the invoker with.</param>
        /// <remarks>
        /// Multiple interceptors can be added on top of each other by calling
        /// "invoker.Intercept(a, b, c)".  The order of invocation will be "a", "b", and then "c".
        /// Interceptors can be later added to an existing intercepted CallInvoker, effectively
        /// building a chain like "invoker.Intercept(c).Intercept(b).Intercept(a)".  Note that
        /// in this case, the last interceptor added will be the first to take control.
        /// </remarks>
        public static CallInvoker Intercept(this CallInvoker invoker, Interceptor interceptor)
        {
            return new InterceptingCallInvoker(invoker, interceptor);
        }

        /// <summary>
        /// Returns a <see cref="Grpc.Core.CallInvoker" /> instance that intercepts
        /// the invoker with the given interceptors.
        /// </summary>
        /// <param name="invoker">The channel to intercept.</param>
        /// <param name="interceptors">
        /// An array of interceptors to intercept the calls to the invoker with.
        /// Control is passed to the interceptors in the order specified.
        /// </param>
        /// <remarks>
        /// Multiple interceptors can be added on top of each other by calling
        /// "invoker.Intercept(a, b, c)".  The order of invocation will be "a", "b", and then "c".
        /// Interceptors can be later added to an existing intercepted CallInvoker, effectively
        /// building a chain like "invoker.Intercept(c).Intercept(b).Intercept(a)".  Note that
        /// in this case, the last interceptor added will be the first to take control.
        /// </remarks>
        public static CallInvoker Intercept(this CallInvoker invoker, params Interceptor[] interceptors)
        {
            GrpcPreconditions.CheckNotNull(invoker, nameof(invoker));
            GrpcPreconditions.CheckNotNull(interceptors, nameof(interceptors));

            foreach (var interceptor in interceptors.Reverse())
            {
                invoker = Intercept(invoker, interceptor);
            }

            return invoker;
        }

        /// <summary>
        /// Returns a <see cref="Grpc.Core.CallInvoker" /> instance that intercepts
        /// the invoker with the given interceptor.
        /// </summary>
        /// <param name="invoker">The underlying invoker to intercept.</param>
        /// <param name="interceptor">
        /// An interceptor delegate that takes the request metadata to be sent with an outgoing call
        /// and returns a <see cref="Grpc.Core.Metadata" /> instance that will replace the existing
        /// invocation metadata.
        /// </param>
        /// <remarks>
        /// Multiple interceptors can be added on top of each other by
        /// building a chain like "invoker.Intercept(c).Intercept(b).Intercept(a)".  Note that
        /// in this case, the last interceptor added will be the first to take control.
        /// </remarks>
        public static CallInvoker Intercept(this CallInvoker invoker, Func<Metadata, Metadata> interceptor)
        {
            return new InterceptingCallInvoker(invoker, new MetadataInterceptor(interceptor));
        }

        private class MetadataInterceptor : Interceptor
        {
            readonly Func<Metadata, Metadata> interceptor;

            /// <summary>
            /// Creates a new instance of MetadataInterceptor given the specified interceptor function.
            /// </summary>
            public MetadataInterceptor(Func<Metadata, Metadata> interceptor)
            {
                this.interceptor = GrpcPreconditions.CheckNotNull(interceptor, nameof(interceptor));
            }

            private ClientInterceptorContext<TRequest, TResponse> GetNewContext<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
                where TRequest : class
                where TResponse : class
            {
                var metadata = context.Options.Headers ?? new Metadata();
                return new ClientInterceptorContext<TRequest, TResponse>(context.Method, context.Host, context.Options.WithHeaders(interceptor(metadata)));
            }

            public override TResponse BlockingUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, BlockingUnaryCallContinuation<TRequest, TResponse> continuation)
            {
                return continuation(request, GetNewContext(context));
            }

            public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
            {
                return continuation(request, GetNewContext(context));
            }

            public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(TRequest request, ClientInterceptorContext<TRequest, TResponse> context, AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
            {
                return continuation(request, GetNewContext(context));
            }

            public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncClientStreamingCallContinuation<TRequest, TResponse> continuation)
            {
                return continuation(GetNewContext(context));
            }

            public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context, AsyncDuplexStreamingCallContinuation<TRequest, TResponse> continuation)
            {
                return continuation(GetNewContext(context));
            }
        }
    }

    /// <summary>
    /// Decorates an underlying <see cref="Grpc.Core.CallInvoker" /> to
    /// intercept calls through a given interceptor.
    /// </summary>
    class InterceptingCallInvoker : CallInvoker
    {
        readonly CallInvoker invoker;
        readonly Interceptor interceptor;

        /// <summary>
        /// Creates a new instance of Grpc.Core.Interceptors.InterceptingCallInvoker
        /// with the given underlying invoker and interceptor instances.
        /// </summary>
        public InterceptingCallInvoker(CallInvoker invoker, Interceptor interceptor)
        {
            this.invoker = GrpcPreconditions.CheckNotNull(invoker, nameof(invoker));
            this.interceptor = GrpcPreconditions.CheckNotNull(interceptor, nameof(interceptor));
        }

        /// <summary>
        /// Intercepts a simple blocking call with the registered interceptor.
        /// </summary>
        public override TResponse BlockingUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return interceptor.BlockingUnaryCall(
                request,
                new ClientInterceptorContext<TRequest, TResponse>(method, host, options),
                (req, ctx) => invoker.BlockingUnaryCall(ctx.Method, ctx.Host, ctx.Options, req));
        }

        /// <summary>
        /// Intercepts a simple asynchronous call with the registered interceptor.
        /// </summary>
        public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return interceptor.AsyncUnaryCall(
                request,
                new ClientInterceptorContext<TRequest, TResponse>(method, host, options),
                (req, ctx) => invoker.AsyncUnaryCall(ctx.Method, ctx.Host, ctx.Options, req));
        }

        /// <summary>
        /// Intercepts an asynchronous server streaming call with the registered interceptor.
        /// </summary>
        public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options, TRequest request)
        {
            return interceptor.AsyncServerStreamingCall(
                request,
                new ClientInterceptorContext<TRequest, TResponse>(method, host, options),
                (req, ctx) => invoker.AsyncServerStreamingCall(ctx.Method, ctx.Host, ctx.Options, req));
        }

        /// <summary>
        /// Intercepts an asynchronous client streaming call with the registered interceptor.
        /// </summary>
        public override AsyncClientStreamingCall<TRequest, TResponse> AsyncClientStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            return interceptor.AsyncClientStreamingCall(
                new ClientInterceptorContext<TRequest, TResponse>(method, host, options),
                ctx => invoker.AsyncClientStreamingCall(ctx.Method, ctx.Host, ctx.Options));
        }

        /// <summary>
        /// Intercepts an asynchronous duplex streaming call with the registered interceptor.
        /// </summary>
        public override AsyncDuplexStreamingCall<TRequest, TResponse> AsyncDuplexStreamingCall<TRequest, TResponse>(Method<TRequest, TResponse> method, string host, CallOptions options)
        {
            return interceptor.AsyncDuplexStreamingCall(
                new ClientInterceptorContext<TRequest, TResponse>(method, host, options),
                ctx => invoker.AsyncDuplexStreamingCall(ctx.Method, ctx.Host, ctx.Options));
        }
    }
}
