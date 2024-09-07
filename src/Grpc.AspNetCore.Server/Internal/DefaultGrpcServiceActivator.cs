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
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal;

internal sealed class DefaultGrpcServiceActivator<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TGrpcService> : IGrpcServiceActivator<TGrpcService> where TGrpcService : class
{
    private static readonly Lazy<ObjectFactory> _objectFactory = new Lazy<ObjectFactory>(static () => ActivatorUtilities.CreateFactory(typeof(TGrpcService), Type.EmptyTypes));

    public GrpcActivatorHandle<TGrpcService> Create(IServiceProvider serviceProvider)
    {
        var service = serviceProvider.GetService<TGrpcService>();
        if (service == null)
        {
            service = (TGrpcService)_objectFactory.Value(serviceProvider, Array.Empty<object>());
            return new GrpcActivatorHandle<TGrpcService>(service, created: true, state: null);
        }

        return new GrpcActivatorHandle<TGrpcService>(service, created: false, state: null);
    }

    public ValueTask ReleaseAsync(GrpcActivatorHandle<TGrpcService> service)
    {
        if (service.Instance == null)
        {
            throw new ArgumentException("Service instance is null.", nameof(service));
        }

        if (service.Created)
        {
            if (service.Instance is IAsyncDisposable asyncDisposableService)
            {
                return asyncDisposableService.DisposeAsync();
            }

            if (service.Instance is IDisposable disposableService)
            {
                disposableService.Dispose();
                return default;
            }
        }

        return default;
    }
}
