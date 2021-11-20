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

using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Grpc.Net.ClientFactory.Internal
{
    // Should be registered as a singleton, so it that it can act as a cache for the Activator.
    internal class DefaultClientActivator<TClient> where TClient : class
    {
        private readonly static Func<ObjectFactory> _createActivator = static () => ActivatorUtilities.CreateFactory(typeof(TClient), new Type[] { typeof(CallInvoker), });

        private readonly IServiceProvider _services;
        private ObjectFactory? _activator;
        private bool _initialized;
        private object? _lock;

        public DefaultClientActivator(IServiceProvider services)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

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

        public TClient CreateClient(CallInvoker callInvoker)
        {
            return (TClient)Activator(_services, new object[] { callInvoker });
        }
    }
}
