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
using Grpc.AspNetCore.Server.Internal;
using Grpc.Shared.Server;
using Microsoft.Extensions.Logging;
using Log = Grpc.AspNetCore.Server.Model.Internal.BinderServiceMethodProviderLog;

namespace Grpc.AspNetCore.Server.Model.Internal;

internal sealed class BinderServiceMethodProvider<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService> : IServiceMethodProvider<TService> where TService : class
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
            // The second parameter is always the service base type
            var serviceParameter = bindMethodInfo.GetParameters()[1];

            var binder = new ProviderServiceBinder<TService>(context, serviceParameter.ParameterType);

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
}

internal static partial class BinderServiceMethodProviderLog
{
    [LoggerMessage(Level = LogLevel.Debug, EventId = 1, EventName = "BindMethodNotFound", Message = "Could not find bind method for {ServiceType}.")]
    public static partial void BindMethodNotFound(ILogger logger, Type serviceType);
}
