#region Copyright notice and license

// Copyright 2015 gRPC authors.
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
using Grpc.AspNetCore.Server.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProtobufServiceDescriptor = Google.Protobuf.Reflection.ServiceDescriptor;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class GrpcRouteBuilderExtensions
    {
        public static IEndpointRouteBuilder MapGrpcService<TImplementation>(this IEndpointRouteBuilder builder) where TImplementation : class
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            // Get implementation type
            var implementationType = typeof(TImplementation);

            // Implementation type FooImpl derives from Foo.FooBase (with implicit base type of Object).
            var baseType = implementationType.BaseType;
            while (baseType.BaseType?.BaseType != null)
            {
                baseType = baseType.BaseType;
            }

            // We need to call Foo.BindService from the declaring type.
            var declaringType = baseType.DeclaringType;

            // Get the descriptor
            var descriptor = declaringType.GetProperty("Descriptor").GetValue(null) as ProtobufServiceDescriptor ?? throw new InvalidOperationException("Cannot retrive service descriptor");

            foreach (var method in descriptor.Methods)
            {
                var inputType = method.InputType;
                var outputType = method.OutputType;
                object handler;

                if (method.IsClientStreaming && method.IsServerStreaming)
                {
                    var handlerType = typeof(DuplexStreamingServerCallHandler<,,>).MakeGenericType(inputType.ClrType, outputType.ClrType, implementationType);
                    handler = Activator.CreateInstance(handlerType, new object[] { inputType.Parser, method.Name });
                }
                else if (method.IsClientStreaming)
                {
                    var handlerType = typeof(ClientStreamingServerCallHandler<,,>).MakeGenericType(inputType.ClrType, outputType.ClrType, implementationType);
                    handler = Activator.CreateInstance(handlerType, new object[] { inputType.Parser, method.Name });
                }
                else if (method.IsServerStreaming)
                {
                    var handlerType = typeof(ServerStreamingServerCallHandler<,,>).MakeGenericType(inputType.ClrType, outputType.ClrType, implementationType);
                    handler = Activator.CreateInstance(handlerType, new object[] { inputType.Parser, method.Name });
                }
                else
                {
                    var handlerType = typeof(UnaryServerCallHandler<,,>).MakeGenericType(inputType.ClrType, outputType.ClrType, implementationType);
                    handler = Activator.CreateInstance(handlerType, new object[] { inputType.Parser, method.Name }) ;
                }

                builder.MapPost($"{method.Service.FullName}/{method.Name}", ((IServerCallHandler)handler).HandleCallAsync);
            }

            return builder;
        }
    }
}
