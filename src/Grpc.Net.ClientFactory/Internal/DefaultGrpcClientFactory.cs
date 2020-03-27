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

using Microsoft.Extensions.DependencyInjection;
using System;

namespace Grpc.Net.ClientFactory.Internal
{
    internal class DefaultGrpcClientFactory : GrpcClientFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public DefaultGrpcClientFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public override TClient? CreateClient<TClient>(string name) where TClient : class
        {
            var typedHttpClientFactory = _serviceProvider.GetService<INamedTypedHttpClientFactory<TClient>>();
            if (typedHttpClientFactory is null)
            {
                ThrowServiceNotConfigured(name);
            }

            if (typedHttpClientFactory!.CanCreateDefaultClient)
            {
                var callInvoker = GetCallInvoker<TClient>(_serviceProvider, name);
                return typedHttpClientFactory.CreateClient(callInvoker);
            }
            return null;
        }
    }
}
