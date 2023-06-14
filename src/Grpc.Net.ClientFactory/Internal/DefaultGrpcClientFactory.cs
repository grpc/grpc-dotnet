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
using Grpc.Core.Interceptors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Grpc.Net.ClientFactory.Internal;

internal class DefaultGrpcClientFactory : GrpcClientFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GrpcCallInvokerFactory _callInvokerFactory;
    private readonly IOptionsMonitor<GrpcClientFactoryOptions> _grpcClientFactoryOptionsMonitor;

    public DefaultGrpcClientFactory(IServiceProvider serviceProvider,
        GrpcCallInvokerFactory callInvokerFactory,
        IOptionsMonitor<GrpcClientFactoryOptions> grpcClientFactoryOptionsMonitor)
    {
        _serviceProvider = serviceProvider;
        _callInvokerFactory = callInvokerFactory;
        _grpcClientFactoryOptionsMonitor = grpcClientFactoryOptionsMonitor;
    }

    public override TClient CreateClient<
#if NET5_0_OR_GREATER
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
#endif
        TClient>(string name) where TClient : class
    {
        var defaultClientActivator = _serviceProvider.GetService<DefaultClientActivator<TClient>>();
        if (defaultClientActivator == null)
        {
            throw new InvalidOperationException($"No gRPC client configured with name '{name}'.");
        }

        var callInvoker = _callInvokerFactory.CreateInvoker(name, typeof(TClient));

        var clientFactoryOptions = _grpcClientFactoryOptionsMonitor.Get(name);

        var resolvedCallInvoker = GrpcClientFactoryOptions.BuildInterceptors(
            callInvoker,
            _serviceProvider,
            clientFactoryOptions,
            InterceptorScope.Client);

#pragma warning disable CS0618 // Type or member is obsolete
        if (clientFactoryOptions.Interceptors.Count != 0)
        {
            resolvedCallInvoker = resolvedCallInvoker.Intercept(clientFactoryOptions.Interceptors.ToArray());
        }
#pragma warning restore CS0618 // Type or member is obsolete

        if (clientFactoryOptions.CallOptionsActions.Count != 0)
        {
            resolvedCallInvoker = new CallOptionsConfigurationInvoker(resolvedCallInvoker, clientFactoryOptions.CallOptionsActions, _serviceProvider);
        }

        if (clientFactoryOptions.Creator != null)
        {
            var c = clientFactoryOptions.Creator(resolvedCallInvoker);
            if (c is TClient client)
            {
                return client;
            }
            else if (c == null)
            {
                throw new InvalidOperationException("A null instance was returned by the configured client creator.");
            }

            throw new InvalidOperationException($"The {c.GetType().FullName} instance returned by the configured client creator is not compatible with {typeof(TClient).FullName}.");
        }
        else
        {
            return defaultClientActivator.CreateClient(resolvedCallInvoker);
        }
    }
}
