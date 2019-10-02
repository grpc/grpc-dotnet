using System.Threading.Tasks;
using Greet;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttributeRouting
{
    [GrpcService]
    public sealed class GreeterWithAttributeRoutingService
    {
        [GrpcMethod]
        public Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, ""));
        }

        //public Task SayHellos(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        //{
        //    throw new RpcException(new Status(StatusCode.Unimplemented, ""));
        //}
    }
}