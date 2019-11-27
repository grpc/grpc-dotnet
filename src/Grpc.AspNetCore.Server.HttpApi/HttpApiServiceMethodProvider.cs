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
using System.Reflection;
using Google.Protobuf.Reflection;
using Grpc.AspNetCore.Server.Model;
using Grpc.Shared.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.HttpApi
{
    internal class HttpApiServiceMethodProvider<TService> : IServiceMethodProvider<TService> where TService : class
    {
        private readonly ILogger<HttpApiServiceMethodProvider<TService>> _logger;
        private readonly GrpcServiceOptions _globalOptions;
        private readonly GrpcServiceOptions<TService> _serviceOptions;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IServiceProvider _serviceProvider;

        public HttpApiServiceMethodProvider(
            ILoggerFactory loggerFactory,
            IOptions<GrpcServiceOptions> globalOptions,
            IOptions<GrpcServiceOptions<TService>> serviceOptions,
            IServiceProvider serviceProvider)
        {
            _logger = loggerFactory.CreateLogger<HttpApiServiceMethodProvider<TService>>();
            _globalOptions = globalOptions.Value;
            _serviceOptions = serviceOptions.Value;
            _loggerFactory = loggerFactory;
            _serviceProvider = serviceProvider;
        }

        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context)
        {
            var bindMethodInfo = BindMethodFinder.GetBindMethod(typeof(TService));

            // Invoke BindService(ServiceBinderBase, BaseType)
            if (bindMethodInfo != null)
            {
                // The second parameter is always the service base type
                var serviceParameter = bindMethodInfo.GetParameters()[1];

                ServiceDescriptor? serviceDescriptor = null;
                try
                {
                    serviceDescriptor = ServiceDescriptorHelpers.GetServiceDescriptor(bindMethodInfo.DeclaringType!);
                }
                catch (Exception ex)
                {
                    Log.ServiceDescriptorError(_logger, typeof(TService), ex);
                }

                if (serviceDescriptor != null)
                {
                    var binder = new HttpApiProviderServiceBinder<TService>(
                        context,
                        serviceParameter.ParameterType,
                        serviceDescriptor,
                        _globalOptions,
                        _serviceOptions,
                        _serviceProvider,
                        _loggerFactory);

                    try
                    {
                        bindMethodInfo.Invoke(null, new object?[] { binder, null });
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Error binding gRPC service '{typeof(TService).Name}'.", ex);
                    }
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
                LoggerMessage.Define<Type>(LogLevel.Warning, new EventId(1, "BindMethodNotFound"), "Could not find bind method for {ServiceType}.");

            private static readonly Action<ILogger, Type, Exception> _serviceDescriptorError =
                LoggerMessage.Define<Type>(LogLevel.Warning, new EventId(2, "ServiceDescriptorError"), "Error getting service descriptor for {ServiceType}.");

            public static void BindMethodNotFound(ILogger logger, Type serviceType)
            {
                _bindMethodNotFound(logger, serviceType, null);
            }

            public static void ServiceDescriptorError(ILogger logger, Type serviceType, Exception ex)
            {
                _serviceDescriptorError(logger, serviceType, ex);
            }
        }
    }
}
