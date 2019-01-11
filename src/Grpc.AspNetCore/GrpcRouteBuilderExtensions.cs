using System;
using Google.Protobuf.Reflection;
using GRPCServer.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GRPCServer.Dotnet
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
            var descriptor = declaringType.GetProperty("Descriptor").GetValue(null) as ServiceDescriptor ?? throw new InvalidOperationException("Cannot retrive service descriptor");

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
