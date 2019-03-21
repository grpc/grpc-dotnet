using Grpc.Core;
using ProtoBuf;
using System;
using System.ServiceModel;
using System.Threading.Tasks;

namespace protobuf_net.Grpc
{
    [ProtoContract]
    class HelloRequest
    {
        [ProtoMember(1)]
        public string Name { get; set; }
    }
    [ProtoContract]
    class HelloReply
    {
        [ProtoMember(1)]
        public string Message { get; set; }
    }

    [ServiceContract(Name = "whatever")]
    class MyService
    {
        [OperationContract(Name = "SayHello")]
        public async Task<HelloReply> SayHello(HelloRequest request)
        {
            await Task.Yield();
            return new HelloReply { Message = "Hello " + request.Name };
        }
    }

    static class Consumer
    {
        static async Task TheirCode()
        {
            var channel = new Channel("localhost:50051", ChannelCredentials.Insecure);

            using (var client = ClientFactory.CreateClient<IMyService>(channel))
            {
                HelloReply response = await client.Channel.SayHelloAsync(new HelloRequest { Name = "abc" });
                Console.WriteLine(response.Message);
            }
        }
    }


    [ServiceContract(Name = "whatever")]
    interface IMyService
    {
        AsyncUnaryCall<HelloReply> SayHelloAsync(HelloRequest request, CallOptions options = default);

        // alternative APIs to recognize and support?
        // Task<HelloReply> SayHelloAsync(HelloRequest request);
        // Task<HelloReply> SayHelloAsync(CancellationToken token);
    }

    class ClientFactory
    {
        public static Client<T> CreateClient<T>(Channel channel) where T : class
        {
            throw new NotImplementedException();
        }
    }
    class Client<T> : IDisposable
    {
        public T Channel { get; }

        void IDisposable.Dispose() { }
    }
}
