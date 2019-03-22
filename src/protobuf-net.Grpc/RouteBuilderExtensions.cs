using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Threading.Tasks;

namespace ProtoBuf.Grpc
{
    public static class RouteBuilderExtensions
    {
        public static IEndpointConventionBuilder MapCodeFirstGrpcService<TService>(this IEndpointRouteBuilder builder)
            where TService : class
        {
            return builder.MapGrpcService<TService>(options =>
            {
                options.BindAction = (binder, service) => DoTheMagic(binder, service);
            });
        }

        private static void DoTheMagic<TService>(ServiceBinderBase binder, TService service)
        {
            var svcType = typeof(TService);
            var sva = (ServiceContractAttribute)Attribute.GetCustomAttribute(svcType, typeof(ServiceContractAttribute));
            var serviceName = sva?.Name;
            if (string.IsNullOrWhiteSpace(serviceName)) serviceName = svcType.FullName.Replace('+','.');

            object[] argsBuffer = null;
            Type[] typesBuffer = null;
            void AddMethod(Type @in, Type @out, MethodInfo m, MethodType t)
            {
                if (typesBuffer == null)
                {
                    typesBuffer = new Type[] { typeof(TService), null, null };
                }
                typesBuffer[1] = @in;
                typesBuffer[2] = @out;

                if (argsBuffer == null)
                {
                    argsBuffer = new object[] { serviceName, null, null, binder, service };
                }
                argsBuffer[1] = m;
                argsBuffer[2] = t;

                s_addMethod.MakeGenericMethod(typesBuffer).Invoke(null, argsBuffer);

            }
            foreach (var method in svcType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var outType = method.ReturnType;
                if (outType == null) continue;
                var args = method.GetParameters();
                if (args.Length == 2 && args[1].ParameterType == typeof(ServerCallContext)
                    && outType.IsGenericType && outType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    outType = outType.GetGenericArguments().Single();
                    var inType = args[0].ParameterType;
                    if (inType.IsGenericType && inType.GetGenericTypeDefinition() == typeof(IAsyncStreamReader<>))
                    {   // Task<TResponse> ClientStreamingServerMethod(IAsyncStreamReader<TRequest> stream, ServerCallContext serverCallContext);
                        inType = inType.GetGenericArguments().Single();
                        AddMethod(inType, outType, method, MethodType.ClientStreaming);

                    }
                    else
                    {   // Task<TResponse> UnaryServerMethod(TRequest request, ServerCallContext serverCallContext);
                        AddMethod(inType, outType, method, MethodType.Unary);
                    }
                }
                else if (args.Length == 3 && args[2].ParameterType == typeof(ServerCallContext) && outType == typeof(Task)
                    && args[1].ParameterType.IsGenericType
                    && args[1].ParameterType.GetGenericTypeDefinition() == typeof(IServerStreamWriter<>))
                {
                    outType = args[1].ParameterType.GetGenericArguments().Single();
                    var inType = args[0].ParameterType;
                    if (inType.IsGenericType && inType.GetGenericTypeDefinition() == typeof(IAsyncStreamReader<>))

                    {
                        inType = inType.GetGenericArguments().Single();
                        // Task DuplexStreamingServerMethod(IAsyncStreamReader<TRequest> input, IServerStreamWriter<TResponse> output, ServerCallContext serverCallContext);
                        AddMethod(inType, outType, method, MethodType.DuplexStreaming);
                    }
                    else
                    {
                        // Task ServerStreamingServerMethod(TRequest request, IServerStreamWriter<TResponse> stream, ServerCallContext serverCallContext);
                        AddMethod(inType, outType, method, MethodType.ServerStreaming);
                    }
                }
            }
        }

        static readonly MethodInfo s_addMethod = typeof(RouteBuilderExtensions).GetMethod(
            nameof(AddMethod), BindingFlags.Static | BindingFlags.NonPublic);
        static void AddMethod<TService, TRequest, TResponse>(
            string serviceName, MethodInfo method, MethodType methodType,
            ServiceBinderBase binder, TService _)
            where TService : class
            where TRequest : class
            where TResponse : class
        {
            var oca = (OperationContractAttribute)Attribute.GetCustomAttribute(method, typeof(OperationContractAttribute));
            var operationName = oca?.Name;
            if (string.IsNullOrWhiteSpace(operationName))
            {
                operationName = method.Name;
                if (operationName.EndsWith("Async")) operationName = operationName.Substring(0, operationName.Length - 5);
            }
            
            switch (methodType)
            {
                case MethodType.Unary:
                    binder.AddMethod(new FullyNamedMethod<TRequest, TResponse>(
                        operationName, methodType, serviceName, method.Name),
                        (UnaryServerMethod<TRequest, TResponse>)null);
                    break;
                case MethodType.ClientStreaming:
                    binder.AddMethod(new FullyNamedMethod<TRequest, TResponse>(
                        operationName, methodType, serviceName, method.Name),
                        (ClientStreamingServerMethod<TRequest, TResponse>)null);
                    break;
                case MethodType.ServerStreaming:
                    binder.AddMethod(new FullyNamedMethod<TRequest, TResponse>(
                        operationName, methodType, serviceName, method.Name),
                        (ServerStreamingServerMethod<TRequest, TResponse>)null);
                    break;
                case MethodType.DuplexStreaming:
                    binder.AddMethod(new FullyNamedMethod<TRequest, TResponse>(
                        operationName, methodType, serviceName, method.Name),
                        (DuplexStreamingServerMethod<TRequest, TResponse>)null);
                    break;
                default:
                    throw new NotSupportedException(methodType.ToString());
            }

        }

        public class FullyNamedMethod<TRequest, TResponse> : Method<TRequest, TResponse>, IMethod
        {
            private readonly string _fullName;

            public FullyNamedMethod(
                string operationName,
                MethodType type,
                string serviceName,
                string methodName,
                Marshaller<TRequest> requestMarshaller = null,
                Marshaller<TResponse> responseMarshaller = null)
                : base(type, serviceName, methodName,
                      requestMarshaller ?? MarshallerCache<TRequest>.Instance,
                      responseMarshaller ?? MarshallerCache<TResponse>.Instance)
            {
                _fullName = serviceName + "/" + operationName;
            }

            string IMethod.FullName => _fullName;
        }

        internal static class MarshallerCache<T>
        {
            public static Marshaller<T> Instance { get; }
                = new Marshaller<T>(Serialize, Deserialize);

            private static T Deserialize(byte[] arg)
            {
                using (var ms = new MemoryStream(arg))
                {
                    return Serializer.Deserialize<T>(ms);
                }
            }

            private static byte[] Serialize(T arg)
            {
                using (var ms = new MemoryStream())
                {
                    Serializer.Serialize(ms, arg);
                    return ms.ToArray();
                }
            }
        }
    }


}
