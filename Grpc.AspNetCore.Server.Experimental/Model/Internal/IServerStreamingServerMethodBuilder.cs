using System.Reflection;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    interface IServerStreamingServerMethodBuilder<TService>
    {
        ServerStreamingServerMethod<TService, TRequest, TResponse> Build<TRequest, TResponse>(MethodInfo method);
    }
}