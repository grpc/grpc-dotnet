using System.Threading.Tasks;
using Greet;
using Grpc.Core;

namespace Grpc.AspNetCore.Server.Tests.TestObjects.Services.WithAttributeRouting
{
    public sealed class EmptyMarshaller<T> : Marshaller<T> where T : new()
    {
        public EmptyMarshaller() : base(t => new byte[0], b => new T()) { }
    }

    [GrpcService]
    public sealed class GreeterWithAttributeRoutingService
    {
        [GrpcMethod]
        [return: GrpcMarshaller(typeof(EmptyMarshaller<HelloReply>))]
        public Task<HelloReply> SayHello([GrpcMarshaller(typeof(EmptyMarshaller<HelloRequest>))] HelloRequest request, ServerCallContext context)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, ""));
        }

        [GrpcMethod(
            ResponseType = typeof(HelloReply),
            ResponseMarshallerType = typeof(EmptyMarshaller<HelloReply>))]
        public Task SayHellos([GrpcMarshaller(typeof(EmptyMarshaller<HelloRequest>))] HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            throw new RpcException(new Status(StatusCode.Unimplemented, ""));
        }
    }
}