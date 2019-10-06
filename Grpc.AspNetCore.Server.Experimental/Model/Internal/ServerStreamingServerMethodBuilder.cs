using System.Reflection;

namespace Grpc.AspNetCore.Server.Model.Internal
{
    public sealed class ServerStreamingServerMethodBuilder<TService> : IServerStreamingServerMethodBuilder<TService>
    {
        public ServerStreamingServerMethod<TService, TRequest, TResponse> Build<TRequest, TResponse>(MethodInfo method)
        {
            throw new System.NotImplementedException();
        }
    }
}