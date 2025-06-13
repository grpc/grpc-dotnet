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
using Grpc.Core;
using Grpc.Shared;
using Microsoft.AspNetCore.Routing;

namespace Grpc.AspNetCore.Server.Model.Internal;

internal sealed class ServiceDefinitionMethodProvider<[DynamicallyAccessedMembers(GrpcProtocolConstants.ServiceAccessibility)] TService> : IServiceMethodProvider<TService> where TService : class
{
    public void OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context)
    {
        if (context.Argument is ServerServiceDefinition serviceDefinition)
        {
            var binder = new ProviderServiceBinder(context);
            serviceDefinition.BindService(binder);
        }
    }

    internal sealed class ProviderServiceBinder : ServiceBinderBase
    {
        private static readonly List<object> _metadata = CreateMetadata();

        private readonly ServiceMethodProviderContext<TService> _context;

        public ProviderServiceBinder(ServiceMethodProviderContext<TService> context)
        {
            _context = context;
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, UnaryServerMethod<TRequest, TResponse>? handler)
        {
            ArgumentNullThrowHelper.ThrowIfNull(handler, nameof(handler));
            _context.AddUnaryMethod(method, _metadata, (service, request, context) => handler(request, context));
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, DuplexStreamingServerMethod<TRequest, TResponse>? handler)
        {
            ArgumentNullThrowHelper.ThrowIfNull(handler, nameof(handler));
            _context.AddDuplexStreamingMethod(method, _metadata, (service, request, response, context) => handler(request, response, context));
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ServerStreamingServerMethod<TRequest, TResponse>? handler)
        {
            ArgumentNullThrowHelper.ThrowIfNull(handler, nameof(handler));
            _context.AddServerStreamingMethod(method, _metadata, (service, request, response, context) => handler(request, response, context));
        }

        public override void AddMethod<TRequest, TResponse>(Method<TRequest, TResponse> method, ClientStreamingServerMethod<TRequest, TResponse>? handler)
        {
            ArgumentNullThrowHelper.ThrowIfNull(handler, nameof(handler));
            _context.AddClientStreamingMethod(method, _metadata, (service, request, context) => handler(request, context));
        }

        private static List<object> CreateMetadata()
        {
            // Accepting CORS preflight means gRPC will allow requests with OPTIONS + preflight headers.
            // If CORS middleware hasn't been configured then the request will reach gRPC handler.
            // gRPC will return 405 response and log that CORS has not been configured.
            var metadata = new List<object>
            {
                new HttpMethodMetadata(["POST"], acceptCorsPreflight: true)
            };
            return metadata;
        }
    }
}
