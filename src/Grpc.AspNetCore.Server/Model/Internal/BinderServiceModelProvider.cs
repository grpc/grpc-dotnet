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
using Microsoft.Extensions.Logging;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal class BinderServiceMethodProvider<TService> : IServiceMethodProvider<TService> where TService : class
    {
        private readonly ILogger<BinderServiceMethodProvider<TService>> _logger;

        public BinderServiceMethodProvider(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<BinderServiceMethodProvider<TService>>();
        }

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context)
        {
            var bindMethodInfo = BindMethodFinder.GetBindMethod(typeof(TService));

            // Invoke BindService(ServiceBinderBase, BaseType)
            if (bindMethodInfo != null)
            {
                var binder = new ProviderServiceBinder<TService>(context);

                try
                {
                    bindMethodInfo.Invoke(null, new object?[] { binder, null });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Error binding gRPC service '{typeof(TService).Name}'.", ex);
                }
            }
            else
            {
                Log.BindMethodNotFound(_logger, typeof(TService));
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Type, Exception?> _bindMethodNotFound =
                LoggerMessage.Define<Type>(LogLevel.Warning, new EventId(1, "BindMethodNotFound"), "Could not find bind method for service '{ServiceType}'.");

            public static void BindMethodNotFound(ILogger logger, Type serviceType)
            {
                _bindMethodNotFound(logger, serviceType, null);
            }
        }
    }
}
