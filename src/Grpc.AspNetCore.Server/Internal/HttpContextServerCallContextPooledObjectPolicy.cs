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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class HttpContextServerCallContextPooledObjectPolicy : PooledObjectPolicy<HttpContextServerCallContext>
    {
        private readonly IServiceProvider _serviceProvider;
        private ObjectPool<HttpContextServerCallContext>? _pool;

        public HttpContextServerCallContextPooledObjectPolicy(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override HttpContextServerCallContext Create()
        {
            if (_pool == null)
            {
                _pool = _serviceProvider.GetRequiredService<ObjectPool<HttpContextServerCallContext>>();
            }

            return new HttpContextServerCallContext(SystemClock.Instance, _pool);
        }

        public override bool Return(HttpContextServerCallContext obj)
        {
            obj.Reset();
            return true;
        }
    }
}
