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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grpc.Net.ClientFactory.Internal
{
    internal class DefaultGrpcClientFactory : GrpcClientFactory
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptionsMonitor<GrpcClientFactoryOptions> _clientOptionsMonitor;

        public DefaultGrpcClientFactory(IServiceProvider serviceProvider, IHttpClientFactory httpClientFactory, IOptionsMonitor<GrpcClientFactoryOptions> clientOptionsMonitor)
        {
            _serviceProvider = serviceProvider;
            _httpClientFactory = httpClientFactory;
            _clientOptionsMonitor = clientOptionsMonitor;
        }

        public override TClient CreateClient<TClient>(string name)
        {
            var options = _clientOptionsMonitor.Get(name);
            if (!options.ExplicitlySet)
            {
                throw new InvalidOperationException($"No gRPC client configured with name '{name}'.");
            }

            var httpClient = _httpClientFactory.CreateClient(name);

            var typedHttpClientFactory = _serviceProvider.GetRequiredService<INamedTypedHttpClientFactory<TClient>>();

            return typedHttpClientFactory.CreateClient(httpClient, name);
        }
    }
}
