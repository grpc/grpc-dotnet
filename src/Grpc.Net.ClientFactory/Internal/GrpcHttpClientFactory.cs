﻿#region Copyright notice and license

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

            var resolvedCallInvoker = options.Interceptors.Count == 0
                ? httpClientCallInvoker
                : httpClientCallInvoker.Intercept(options.Interceptors.ToArray());

            return (TClient)_cache.Activator(_services, new object[] { resolvedCallInvoker });
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
