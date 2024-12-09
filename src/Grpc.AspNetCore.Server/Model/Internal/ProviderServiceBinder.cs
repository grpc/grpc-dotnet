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
using System.Reflection;
using Grpc.AspNetCore.Server.Internal;
using Grpc.Core;
using Microsoft.AspNetCore.Routing;

namespace Grpc.AspNetCore.Server.Model.Internal;

internal sealed class ProviderServiceBinder<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService> : ServiceBinderBase where TService : class
{
    private readonly ServiceMethodProviderContext<TService> _context;
    private readonly Type _declaringType;

    internal ProviderServiceBinder(ServiceMethodProviderContext<TService> context, Type declaringType)
    {
        _context = context;
        _declaringType = declaringType;
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse>? handler)
    {
        var (invoker, metadata) = CreateModelCore<ClientStreamingServerMethod<TService, TRequest, TResponse>>(
            method.Name,
            new[] { typeof(IAsyncStreamReader<TRequest>), typeof(ServerCallContext) });

        _context.AddClientStreamingMethod<TRequest, TResponse>(method, metadata, invoker);
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TRequest, TResponse>? handler)
    {
        var (invoker, metadata) = CreateModelCore<DuplexStreamingServerMethod<TService, TRequest, TResponse>>(
            method.Name,
            new[] { typeof(IAsyncStreamReader<TRequest>), typeof(IServerStreamWriter<TResponse>), typeof(ServerCallContext) });

        _context.AddDuplexStreamingMethod<TRequest, TResponse>(method, metadata, invoker);
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse>? handler)
    {
        var (invoker, metadata) = CreateModelCore<ServerStreamingServerMethod<TService, TRequest, TResponse>>(
            method.Name,
            new[] { typeof(TRequest), typeof(IServerStreamWriter<TResponse>), typeof(ServerCallContext) });

        _context.AddServerStreamingMethod<TRequest, TResponse>(method, metadata, invoker);
    }

    public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse>? handler)
    {
        var (invoker, metadata) = CreateModelCore<UnaryServerMethod<TService, TRequest, TResponse>>(
            method.Name,
            new[] { typeof(TRequest), typeof(ServerCallContext) });

        _context.AddUnaryMethod<TRequest, TResponse>(method, metadata, invoker);
    }

    private (TDelegate invoker, List<object> metadata) CreateModelCore<TDelegate>(string methodName, Type[] methodParameters) where TDelegate : Delegate
    {
        var handlerMethod = GetMethod(methodName, methodParameters);

        if (handlerMethod == null)
        {
            throw new InvalidOperationException($"Could not find '{methodName}' on {typeof(TService)}.");
        }

        var invoker = (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), handlerMethod);

        var metadata = new List<object>();
        // Add type metadata first so it has a lower priority
        metadata.AddRange(typeof(TService).GetCustomAttributes(inherit: true));
        // Add method metadata last so it has a higher priority
        metadata.AddRange(handlerMethod.GetCustomAttributes(inherit: true));

        // Accepting CORS preflight means gRPC will allow requests with OPTIONS + preflight headers.
        // If CORS middleware hasn't been configured then the request will reach gRPC handler.
        // gRPC will return 405 response and log that CORS has not been configured.
        metadata.Add(new HttpMethodMetadata(new[] { "POST" }, acceptCorsPreflight: true));

        return (invoker, metadata);
    }

    private MethodInfo? GetMethod(string methodName, Type[] methodParameters)
    {
        Type? currentType = typeof(TService);
        while (currentType != null)
        {
            // Specify binding flags explicitly because we don't want to match static methods.
            var matchingMethod = currentType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: methodParameters,
                modifiers: null);

            if (matchingMethod == null)
            {
                return null;
            }

            // Validate that the method overrides the virtual method on the base service type.
            // If there is a method with the same name it will hide the base method. Ignore it,
            // and continue searching on the base type.
            if (matchingMethod.IsVirtual)
            {
                var baseDefinitionMethod = matchingMethod.GetBaseDefinition();
                if (baseDefinitionMethod != null && baseDefinitionMethod.DeclaringType == _declaringType)
                {
                    return matchingMethod;
                }
            }

            currentType = currentType.BaseType;
        }

        return null;
    }
}
