using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    internal sealed class UnaryServerMethodBuilder<TService> : IUnaryServerMethodBuilder<TService>
    {
        static readonly MethodInfo? GetServiceMethod = typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService));
        static readonly MethodInfo? GetHttpContextMethod = typeof(ServerCallContextExtensions).GetMethod(nameof(ServerCallContextExtensions.GetHttpContext));

        public UnaryServerMethod<TService, TRequest, TResponse> Build<TRequest, TResponse>(MethodInfo method)
        {
            return new UnaryServerMethod<TService, TRequest, TResponse>(CreateExpression<TRequest, TResponse>(method).Compile());
        }

        static Expression<Func<TService, TRequest, ServerCallContext, Task<TResponse>>> CreateExpression<TRequest, TResponse>(MethodInfo method)
        {
            var serviceParameter = Expression.Parameter(typeof(TService));
            var requestParameter = Expression.Parameter(typeof(TRequest));
            var serverCallContextParameter = Expression.Parameter(typeof(ServerCallContext));

            var serviceProviderVariable = Expression.Variable(typeof(IServiceProvider));

            var arguments = new List<Expression>();
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(TRequest))
                {
                    arguments.Add(requestParameter);
                    continue;
                }

                if (parameter.ParameterType == typeof(ServerCallContext))
                {
                    arguments.Add(serverCallContextParameter);
                    continue;
                }

                if (Attribute.IsDefined(parameter, typeof(FromServiceAttribute)))
                {
                    arguments.Add(
                        Expression.Convert(
                            Expression.Call(
                                serviceProviderVariable,
                                GetServiceMethod,
                                Expression.Constant(parameter.ParameterType)),
                            parameter.ParameterType));

                    continue;
                }

                throw new InvalidOperationException("Unsupported parameter.");
            }

            var blockExpression = Expression.Block(
                new[] { serviceProviderVariable },
                Expression.Assign(
                    serviceProviderVariable,
                    Expression.PropertyOrField(
                        Expression.Call(GetHttpContextMethod, serverCallContextParameter), nameof(HttpContext.RequestServices))),
                Expression.Call(serviceParameter, method, arguments));

            return Expression.Lambda<Func<TService, TRequest, ServerCallContext, Task<TResponse>>>(
                blockExpression,
                serviceParameter,
                requestParameter,
                serverCallContextParameter);
        }
    }
}