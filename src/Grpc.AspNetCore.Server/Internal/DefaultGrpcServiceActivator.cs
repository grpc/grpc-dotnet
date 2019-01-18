#region Copyright notice and license

// Copyright 2015 gRPC authors.
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
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.AspNetCore.Server.Internal
{
    internal class DefaultGrpcServiceActivator<TGrpcService> : IGrpcServiceActivator<TGrpcService> where TGrpcService : class
    {
        private static readonly Lazy<ObjectFactory> _objectFactory = new Lazy<ObjectFactory>(() => ActivatorUtilities.CreateFactory(typeof(TGrpcService), Type.EmptyTypes));
        private readonly IServiceProvider _serviceProvider;
        private bool? _created;

        public DefaultGrpcServiceActivator(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public TGrpcService Create()
        {
            Debug.Assert(!_created.HasValue, "Grpc service activator must not be reused.");

            _created = false;
            var service = _serviceProvider.GetService<TGrpcService>();
            if (service == null)
            {
                service = (TGrpcService)_objectFactory.Value(_serviceProvider, Array.Empty<object>());
                _created = true;
            }

            return service;
        }

        public void Release(TGrpcService service)
        {
        }
    }
}
