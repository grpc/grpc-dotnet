using System.Collections.Generic;
using System.Reflection;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal sealed class PocoServiceMethodProvider<TService> : IServiceMethodProvider<TService> where TService : class
    {
        public void OnServiceMethodDiscovery(ServiceMethodProviderContext<TService> context)
        {
            var serviceAttribute = typeof(TService).GetCustomAttribute<GrpcServiceAttribute>();

            if (serviceAttribute == null)
            {
                // we should allow this and treat the GrpcServiceAttribute as an override to the default 
                // conventions but that means that we could potentially duplicate the methods as the 
                // binder service works first
                return;
            }

            var serviceName = serviceAttribute.Name ?? typeof(TService).FullName;

            foreach (var (serviceMethod, serviceMethodAttribute) in FindMethods())
            {
                MethodInfo method;
                switch (serviceMethodAttribute.MethodType ?? GuessMethodType(serviceMethod))
                {
                    //case MethodType.Unary:
                    //    method = RegisterUnaryMethod
                    //        .MakeGenericMethod(
                    //            serviceMethodAttribute.RequestType ?? FindRequestType(MethodType.Unary, serviceMethod),
                    //            serviceMethodAttribute.ResponseType ?? FindResponseType(MethodType.Unary, serviceMethod));
                    //    break;

                    //case MethodType.ClientStreaming:
                    //case MethodType.DuplexStreaming:
                    //case MethodType.ServerStreaming:
                    default:
                        continue;
                }

                method.Invoke(null, new object[] { context, serviceName, serviceMethod });
            }
        }

        static MethodType GuessMethodType(MethodInfo method)
        {
            // TODO: guess the method type based on the method signature
            return MethodType.Unary;
        }

        static IEnumerable<(MethodInfo, GrpcMethodAttribute)> FindMethods()
        {
            foreach (var method in typeof(TService).GetRuntimeMethods())
            {
                var attribute = method.GetCustomAttribute<GrpcMethodAttribute>();

                if (attribute != null)
                {
                    yield return (method, attribute);
                }
            }
        }
    }
}