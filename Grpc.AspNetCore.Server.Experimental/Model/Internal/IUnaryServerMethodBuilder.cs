using System.Reflection;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    interface IUnaryServerMethodBuilder<TService>
    {
        UnaryServerMethod<TService, TRequest, TResponse> Build<TRequest, TResponse>(MethodInfo method);
    }
}