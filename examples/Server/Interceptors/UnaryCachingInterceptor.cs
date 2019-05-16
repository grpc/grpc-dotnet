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
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Server.Interceptors
{
    public class UnaryCachingInterceptor : Interceptor
    {
        // Using a static cache so this interceptor can be registered with a Scoped or Singleton lifetime
        private static readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
        private readonly ILogger _logger;

        public UnaryCachingInterceptor(ILogger<UnaryCachingInterceptor> logger)
        {
            _logger = logger;
        }

        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
        {
            if (_cache.TryGetValue<TResponse>(request, out var cachedResponse))
            {
                _logger.LogDebug("Cache hit");
                return cachedResponse;
            }
            else
            {
                _logger.LogDebug("Cache miss");

                var response = await continuation(request, context);

                _cache.Set(request, response, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromSeconds(10)
                });

                return response;
            }
        }
    }
}
