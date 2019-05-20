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
using Grpc.AspNetCore.Server.Features;
using Grpc.Core;
using Grpc.NetCore.HttpClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.GrpcClient.Internal
{
    internal class GrpcHttpClientFactory<TClient> : INamedTypedHttpClientFactory<TClient> where TClient : ClientBase
    {
        private readonly Cache _cache;
        private readonly IServiceProvider _services;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IOptionsMonitor<GrpcClientOptions> _clientOptions;
        private readonly ILoggerFactory _loggerFactory;

        public GrpcHttpClientFactory(Cache cache, IServiceProvider services, IOptionsMonitor<GrpcClientOptions> clientOptions, ILoggerFactory loggerFactory)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (clientOptions == null)
            {
                throw new ArgumentNullException(nameof(clientOptions));
            }

            if (loggerFactory == null)
            {
                throw new ArgumentNullException(nameof(loggerFactory));
            }

            _cache = cache;
            _services = services;
            _httpContextAccessor = services.GetService<IHttpContextAccessor>();
            _clientOptions = clientOptions;
            _loggerFactory = loggerFactory;
        }

        public TClient CreateClient(HttpClient httpClient, string name)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            var callInvoker = new HttpClientCallInvoker(httpClient, _loggerFactory);

            var httpContext = _httpContextAccessor.HttpContext;
            var serverCallContext = httpContext?.Features.Get<IServerCallContextFeature>().ServerCallContext;

            var namedOptions = _clientOptions.Get(name);

            if (namedOptions.PropagateCancellationToken)
            {
                if (serverCallContext == null)
                {
                    throw new InvalidOperationException("Cannot propagate the call cancellation token to the client. Cannot find the current gRPC ServerCallContext.");
                }

                callInvoker.CancellationToken = serverCallContext.CancellationToken;
            }

            if (namedOptions.PropagateDeadline)
            {
                if (serverCallContext == null)
                {
                    throw new InvalidOperationException("Cannot propagate the call deadline to the client. Cannot find the current gRPC ServerCallContext.");
                }

                callInvoker.Deadline = serverCallContext.Deadline;
            }

            return (TClient)_cache.Activator(_services, new object[] { callInvoker });
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
}
