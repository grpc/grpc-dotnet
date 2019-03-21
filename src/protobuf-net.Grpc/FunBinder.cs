using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProtoBuf;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.ServiceModel;
using System.Threading.Tasks;

namespace protobuf_net.Grpc
{
    public static class FunBinder
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
            if (string.IsNullOrWhiteSpace(serviceName)) serviceName = svcType.Name;

            object[] argsBuffer = null;
            Type[] typesBuffer = null;
            Type[] WithTypes(Type @in, Type @out)
            {
                if (typesBuffer == null)
                {
                    typesBuffer = new Type[] { typeof(TService), null, null };
                }
                typesBuffer[1] = @in;
                typesBuffer[2] = @out;
                return typesBuffer;
            }
            object[] WithMethod(MethodInfo m)
            {
                if (argsBuffer == null)
                {
                    argsBuffer = new object[] { serviceName, null, binder, service };
                }
                argsBuffer[1] = m;
                return argsBuffer;
            }
            foreach (var method in svcType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var outType = method.ReturnType;
                if (outType == null) continue;
                var args = method.GetParameters();
                if (args.Length == 2 && args[1].ParameterType == typeof(ServerCallContext))
                {
                    var inType = args[0].ParameterType;
                    if (outType.IsGenericType && outType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        outType = outType.GetGenericArguments().Single();
                        s_addUnaryMethod.MakeGenericMethod(WithTypes(inType, outType)).Invoke(null, WithMethod(method));
                    }
                }
                else if (args.Length == 3 && args[2].ParameterType == typeof(ServerCallContext) && outType == typeof(Task))
                {
                    var inType = args[0].ParameterType;
                    outType = args[1].ParameterType;
                    if (outType.IsGenericType && outType.GetGenericTypeDefinition() == typeof(IServerStreamWriter<>))
                    {
                        outType = outType.GetGenericArguments().Single();
                        s_addServerStreamingMethod.MakeGenericMethod(WithTypes(inType, outType)).Invoke(null, WithMethod(method));
                    }
                }
                
            }
        }
        static string GetName(MethodInfo method)
        {
            var oca = (OperationContractAttribute)Attribute.GetCustomAttribute(method, typeof(OperationContractAttribute));
            var name = oca?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = method.Name;
                if (name.EndsWith("Async")) name = name.Substring(0, name.Length - 5);
            }
            return name;
        }
        static readonly MethodInfo s_addUnaryMethod = typeof(FunBinder).GetMethod(
            nameof(AddUnaryMethod), BindingFlags.Static | BindingFlags.NonPublic);
        static readonly MethodInfo s_addServerStreamingMethod = typeof(FunBinder).GetMethod(
            nameof(AddServerStreamingMethod), BindingFlags.Static | BindingFlags.NonPublic);
        static void AddUnaryMethod<TService, TRequest, TResponse>(
            string serviceName, MethodInfo method,
            ServiceBinderBase binder, TService _)
            where TService : class
            where TRequest : class
            where TResponse : class
        {
            binder.AddMethod(new FullyNamedMethod<TRequest, TResponse>(
                serviceName + "/" + GetName(method),
                MethodType.Unary, serviceName, method.Name,
                MarshallerCache<TRequest>.Instance,
                MarshallerCache<TResponse>.Instance), (UnaryServerMethod<TRequest, TResponse>)null);
        }
        static void AddServerStreamingMethod<TService, TRequest, TResponse>(
            string serviceName, MethodInfo method,
            ServiceBinderBase binder, TService _)
            where TService : class
            where TRequest : class
            where TResponse : class
        {
            binder.AddMethod(new FullyNamedMethod<TRequest, TResponse>(
                serviceName + "/" + GetName(method),
                MethodType.ServerStreaming, serviceName, method.Name,
                MarshallerCache<TRequest>.Instance,
                MarshallerCache<TResponse>.Instance), (ServerStreamingServerMethod<TRequest, TResponse>)null);
        }

        public class FullyNamedMethod<TRequest, TResponse> : Method<TRequest, TResponse>, IMethod
        {
            private readonly string _fullName;

            public FullyNamedMethod(
                string fullName,
                MethodType type,
                string serviceName,
                string name,
                Marshaller<TRequest> requestMarshaller,
                Marshaller<TResponse> responseMarshaller)
                : base(type, serviceName, name, requestMarshaller, responseMarshaller)
            {
                _fullName = fullName;
            }

            string IMethod.FullName => _fullName;
        }

        static class MarshallerCache<T>
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
