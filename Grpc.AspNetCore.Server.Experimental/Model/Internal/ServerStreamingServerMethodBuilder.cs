using System;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    public sealed class ServerStreamingServerMethodBuilder<TService> : IServerStreamingServerMethodBuilder<TService>
    {
        public ServerStreamingServerMethod<TService, TRequest, TResponse> Build<TRequest, TResponse>(MethodInfo method)
        {
            return new ServerStreamingServerMethod<TService, TRequest, TResponse>(CreateExpression<TRequest, TResponse>(method).Compile());
        }

        static Expression<Func<TService, TRequest, IServerStreamWriter<TResponse>, ServerCallContext, Task>> CreateExpression<TRequest, TResponse>(MethodInfo method)
        {
            // TODO
            return (service, request, writer, context) => Task.CompletedTask;
        }
    }
}