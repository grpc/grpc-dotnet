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
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Grpc.Core;
using Grpc.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Grpc.AspNetCore.Server.Reflection.Internal
{
    internal class ReflectionGrpcServiceActivator : IGrpcServiceActivator<ReflectionServiceImpl>
    {
        private readonly ILogger<ReflectionGrpcServiceActivator> _logger;
        private readonly EndpointDataSource _endpointDataSource;

        private ReflectionServiceImpl? _instance;

        public ReflectionGrpcServiceActivator(EndpointDataSource endpointDataSource, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<ReflectionGrpcServiceActivator>();
            _endpointDataSource = endpointDataSource;
        }

        public ReflectionServiceImpl Create()
        {
            if (_instance == null)
            {
                var grpcEndpointMetadata = _endpointDataSource.Endpoints
                    .Select(ep => ep.Metadata.GetMetadata<GrpcMethodMetadata>())
                    .Where(m => m != null)
                    .ToList();

                var serviceTypes = grpcEndpointMetadata.Select(m => m.ServiceType).Distinct().ToList();

                var serviceDescriptors = new List<Google.Protobuf.Reflection.ServiceDescriptor>();

                foreach (var serviceType in serviceTypes)
                {
                    var baseType = GetServiceBaseType(serviceType);
                    var definitionType = baseType?.DeclaringType;

                    var descriptorPropertyInfo = definitionType?.GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static);
                    if (descriptorPropertyInfo != null)
                    {
                        var serviceDescriptor = descriptorPropertyInfo.GetValue(null) as Google.Protobuf.Reflection.ServiceDescriptor;
                        if (serviceDescriptor != null)
                        {
                            serviceDescriptors.Add(serviceDescriptor);
                            continue;
                        }
                    }

                    Log.ServiceDescriptorNotResolved(_logger, serviceType);
                }

                _instance = new ReflectionServiceImpl(serviceDescriptors);
            }

            return _instance;
        }

        private static Type? GetServiceBaseType(Type serviceImplementation)
        {
            // TService is an implementation of the gRPC service. It ultimately derives from Foo.TServiceBase base class.
            // We need to access the static BindService method on Foo which implicitly derives from Object.
            var baseType = serviceImplementation.BaseType;

            // Handle services that have multiple levels of inheritence
            while (baseType?.BaseType?.BaseType != null)
            {
                baseType = baseType.BaseType;
            }

            return baseType;
        }

        public void Release(ReflectionServiceImpl service)
        {
        }

        private static class Log
        {
            private static readonly Action<ILogger, string, Exception?> _serviceDescriptorNotResolved =
                LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "ServiceDescriptorNotResolved"), "Could not resolve service descriptor for '{ServiceType}'. The service metadata will not be exposed by the reflection service.");

            public static void ServiceDescriptorNotResolved(ILogger logger, Type serviceType)
            {
                _serviceDescriptorNotResolved(logger, serviceType.FullName, null);
            }
        }
    }
}
