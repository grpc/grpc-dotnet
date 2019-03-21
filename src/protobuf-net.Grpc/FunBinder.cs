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
            foreach (var method in svcType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {

                var args = method.GetParameters();
                if (args.Length != 2 || args[1].ParameterType != typeof(ServerCallContext)) continue;
                var inType = args[0].ParameterType;

                var outType = method.ReturnType;
                if (outType == null || !outType.IsGenericType || outType.GetGenericTypeDefinition() != typeof(Task<>))
                    continue; // expect Task<T> result - we want the T
                outType = outType.GetGenericArguments().Single();

                if(argsBuffer == null)
                {
                    argsBuffer = new object[] { serviceName, null, binder, service };
                }
                argsBuffer[1] = method;
                s_addMethod.MakeGenericMethod(typeof(TService), inType, outType)
                    .Invoke(null, argsBuffer);
            }
        }
        static readonly MethodInfo s_addMethod = typeof(FunBinder).GetMethod(
            nameof(AddMethod), BindingFlags.Static | BindingFlags.NonPublic);
        static void AddMethod<TService, TRequest, TResponse>(
            string serviceName, MethodInfo method,
            ServiceBinderBase binder, TService service)
            where TService : class
            where TRequest : class
            where TResponse : class
        {
            UnaryServerMethod<TRequest, TResponse> handler = null;
            var oca = (OperationContractAttribute)Attribute.GetCustomAttribute(method, typeof(OperationContractAttribute));
            var name = oca?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = method.Name;
                if (name.EndsWith("Async")) name = name.Substring(0, name.Length - 5);
            }

            binder.AddMethod(new Method<TRequest, TResponse>(
                MethodType.Unary, serviceName, name,
                MarshallerCache<TRequest>.Instance,
                MarshallerCache<TResponse>.Instance), handler);
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
