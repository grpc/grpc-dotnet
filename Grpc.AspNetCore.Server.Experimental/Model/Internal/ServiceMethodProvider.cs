using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal sealed class ServiceMethodProvider<TService> : IServiceMethodProvider<TService> where TService : class
    {
        static readonly MethodInfo RegisterUnaryMethod = FindMethod(nameof(AddUnaryMethod));
        static readonly MethodInfo RegisterServerStreamingMethod = FindMethod(nameof(AddServerStreamingMethod));

        readonly IUnaryServerMethodBuilder<TService> _unaryServerMethodBuilder;
        readonly IServerStreamingServerMethodBuilder<TService> _serverStreamingSeverMethodBuilder;

        public ServiceMethodProvider(
            IUnaryServerMethodBuilder<TService> unaryServerMethodBuilder,
            IServerStreamingServerMethodBuilder<TService> serverStreamingSeverMethodBuilder)
        {
            _unaryServerMethodBuilder = unaryServerMethodBuilder;
            _serverStreamingSeverMethodBuilder = serverStreamingSeverMethodBuilder;
        }

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

            if (serviceName == null)
            {
                throw new ArgumentException("Could not determine Service Name.");
            }

            foreach (var (serviceMethod, serviceMethodAttribute) in FindMethods())
            {
                MethodInfo method;
                switch (serviceMethodAttribute.MethodType ?? GuessMethodType(serviceMethod))
                {
                    case MethodType.Unary:
                        method = RegisterUnaryMethod
                            .MakeGenericMethod(
                                serviceMethodAttribute.RequestType ?? FindRequestType(MethodType.Unary, serviceMethod),
                                serviceMethodAttribute.ResponseType ?? FindResponseType(MethodType.Unary, serviceMethod));
                        break;

                    case MethodType.ServerStreaming:
                        method = RegisterServerStreamingMethod
                            .MakeGenericMethod(
                                serviceMethodAttribute.RequestType ?? FindRequestType(MethodType.ServerStreaming, serviceMethod),
                                serviceMethodAttribute.ResponseType ?? FindResponseType(MethodType.ServerStreaming, serviceMethod));
                        break;

                    //case MethodType.ClientStreaming:
                    //case MethodType.DuplexStreaming:
                    default:
                        continue;
                }

                method.Invoke(this, new object[] { context, serviceName, serviceMethod });
            }
        }

        void AddUnaryMethod<TRequest, TResponse>(ServiceMethodProviderContext<TService> context, string serviceName, MethodInfo method) 
            where TRequest : class 
            where TResponse : class
        {
            var serviceMethodAttribute = method.GetCustomAttribute<GrpcMethodAttribute>();

            var unaryMethod = new Method<TRequest, TResponse>(
                MethodType.Unary,
                serviceName,
                serviceMethodAttribute?.Name ?? method.Name,
                CreateRequestMarshaller<TRequest>(
                    serviceMethodAttribute?.RequestMarshallerType ?? FindRequestMarshaller<TRequest>(method)),
                CreateResponseMarshaller<TResponse>(
                    serviceMethodAttribute?.ResponseMarshallerType ?? FindResponseMarshaller<TResponse>(method)));

            context.AddUnaryMethod(
                unaryMethod,
                Array.Empty<object>(),
                _unaryServerMethodBuilder.Build<TRequest, TResponse>(method),
                options =>
                {
                    foreach (var interceptor in FindInterceptors(method))
                    {
                        options.Interceptors.Add(interceptor.InterceptorType, interceptor.Args);
                    }
                });
        }

        void AddServerStreamingMethod<TRequest, TResponse>(ServiceMethodProviderContext<TService> context, string serviceName, MethodInfo method)
            where TRequest : class
            where TResponse : class
        {
            var serviceMethodAttribute = method.GetCustomAttribute<GrpcMethodAttribute>();

            var serverStreamingMethod = new Method<TRequest, TResponse>(
                MethodType.ServerStreaming,
                serviceName,
                serviceMethodAttribute?.Name ?? method.Name,
                CreateRequestMarshaller<TRequest>(
                    serviceMethodAttribute?.RequestMarshallerType ?? FindRequestMarshaller<TRequest>(method)),
                CreateResponseMarshaller<TResponse>(
                    serviceMethodAttribute?.ResponseMarshallerType ?? FindResponseMarshaller<TResponse>(method)));

            context.AddServerStreamingMethod(
                serverStreamingMethod,
                Array.Empty<object>(),
                _serverStreamingSeverMethodBuilder.Build<TRequest, TResponse>(method),
                options =>
                {
                    foreach (var interceptor in FindInterceptors(method))
                    {
                        options.Interceptors.Add(interceptor.InterceptorType, interceptor.Args);
                    }
                });
        }

        static Type FindRequestMarshaller<TRequest>(MethodInfo method)
        {
            var parameter = method.GetParameters().SingleOrDefault(p => p.ParameterType == typeof(TRequest));

            if (parameter == null)
            {
                throw CouldNotCreateRequestMarshallerException();
            }

            var attribute = parameter.GetCustomAttribute<GrpcMarshallerAttribute>();

            if (attribute == null)
            {
                throw CouldNotCreateRequestMarshallerException();
            }

            return attribute.MarshallerType;

            static Exception CouldNotCreateRequestMarshallerException() => new InvalidOperationException("Could not find a type to use as the Request Marshaller.");
        }

        static Marshaller<TRequest> CreateRequestMarshaller<TRequest>(Type type)
        {
            var marshaller = CreateMarshaller<TRequest>(type);

            if (marshaller == null)
            {
                throw CouldNotCreateRequestMarshallerException();
            }

            return marshaller;

            static Exception CouldNotCreateRequestMarshallerException() => new InvalidOperationException("Could not create an instance of a Request Marshaller.");
        }

        static Type FindResponseMarshaller<TResponse>(MethodInfo method)
        {
            if (method.ReturnParameter == null)
            {
                throw CouldNotCreateResponseMarshallerException();
            }

            var attribute = method.ReturnParameter.GetCustomAttribute<GrpcMarshallerAttribute>();

            if (attribute == null)
            {
                throw CouldNotCreateResponseMarshallerException();
            }

            return attribute.MarshallerType;

            static Exception CouldNotCreateResponseMarshallerException() => new InvalidOperationException("Could find a type to use as the Response Marshaller.");
        }

        static Marshaller<TResponse> CreateResponseMarshaller<TResponse>(Type type)
        {
            var marshaller = CreateMarshaller<TResponse>(type);

            if (marshaller == null)
            {
                throw CouldNotCreateResponseMarshallerException();
            }

            return marshaller;

            static Exception CouldNotCreateResponseMarshallerException() => new InvalidOperationException("Could create an instance of a Response Marshaller.");
        }

        static Marshaller<T>? CreateMarshaller<T>(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return Activator.CreateInstance(type) as Marshaller<T>;
        }

        static MethodType GuessMethodType(MethodInfo method)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType.IsServerStreamWriter())
                {
                    return MethodType.ServerStreaming;
                }
            }

            return MethodType.Unary;
        }

        static MethodInfo FindMethod(string name)
        {
            const BindingFlags bindingFlags = BindingFlags.NonPublic | BindingFlags.Instance;

            var method = typeof(ServiceMethodProvider<TService>).GetMethod(name, bindingFlags);

            if (method == null)
            {
                throw new InvalidOperationException($"Method {name} could not be found.");
            }

            return method;
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

        static Type FindRequestType(MethodType methodType, MethodInfo method)
        {
            foreach (var parameter in method.GetParameters())
            {
                if (Attribute.IsDefined(parameter, typeof(FromServiceAttribute)))
                {
                    continue;
                }

                if (parameter.ParameterType == typeof(ServerCallContext))
                {
                    continue;
                }

                return parameter.ParameterType;
            }

            throw new InvalidOperationException("Could not find a parameter to use as the request type.");
        }

        static Type FindResponseType(MethodType methodType, MethodInfo method)
        {
            var type = method.ReturnType;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
            {
                type = type.GetGenericArguments()[0];
            }

            return type;
        }

        static IEnumerable<InterceptorAttribute> FindInterceptors(MemberInfo method)
        {
            return method.GetCustomAttributes<InterceptorAttribute>();
        }
    }
}