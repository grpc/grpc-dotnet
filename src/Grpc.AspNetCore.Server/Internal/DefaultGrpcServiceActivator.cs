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
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class DefaultGrpcServiceActivator : IGrpcServiceActivator
    {
        private readonly ConcurrentDictionary<Type, ObjectFactory> _objectFactories =
            new ConcurrentDictionary<Type, ObjectFactory>();

        public object Create(ServerCallContext context, Type grpcServiceType)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (grpcServiceType == null)
            {
                throw new ArgumentNullException(nameof(grpcServiceType));
            }

            ObjectFactory factory = _objectFactories.GetOrAdd(grpcServiceType, this.CreateObjectFactory);

            return factory(context.GetHttpContext().RequestServices, Array.Empty<object>());
        }

        public ValueTask ReleaseAsync(object grpcServiceInstance)
        {
            if (grpcServiceInstance == null)
            {
                throw new ArgumentException("Service instance is null.", nameof(grpcServiceInstance));
            }

            if (grpcServiceInstance is IAsyncDisposable asyncDisposableService)
            {
                return asyncDisposableService.DisposeAsync();
            }

            if (grpcServiceInstance is IDisposable disposableService)
            {
                disposableService.Dispose();
            }

            return default;
        }

        private ObjectFactory CreateObjectFactory(Type grpcServiceType) =>
            ActivatorUtilities.CreateFactory(grpcServiceType, Type.EmptyTypes);
    }
}
