using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using ProtoBuf;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

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
            foreach (var method in svcType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                // if(Attribute.IsDefined(typeof(OperationContract)))
                var args = method.GetParameters();
                var inType = args[0].ParameterType;
                var outType = method.ReturnType;

                s_do.MakeGenericMethod(typeof(TService), inType, outType)
                    .Invoke(null, new object[] { svcType, method, binder, service });
            }
        }
        static readonly MethodInfo s_do = typeof(FunBinder).GetMethod(
            nameof(Do), BindingFlags.Static | BindingFlags.NonPublic);
        static void Do<TService, TRequest, TResponse>(
            Type serviceType, MethodInfo method,
            ServiceBinderBase binder, TService service)
            where TService : class
            where TRequest : class
            where TResponse : class
        {
            UnaryServerMethod<TRequest, TResponse> handler = null;
            binder.AddMethod(new Method<TRequest, TResponse>(
                MethodType.Unary, serviceType.Name, method.Name,
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
