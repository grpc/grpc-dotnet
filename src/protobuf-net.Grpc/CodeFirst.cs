// this is just me thinking aloud; "how would someone expect to use this?"

using Grpc.Core;
using ProtoBuf;
using System;
using System.Runtime.CompilerServices;
using System.ServiceModel;
using System.Threading.Tasks;
using static ProtoBuf.Grpc.RouteBuilderExtensions;

namespace BrainDumpOfIdeas
{
    [ProtoContract]
    public class HelloRequest
    {
        [ProtoMember(1)]
        public string Name { get; set; }
    }
    [ProtoContract]
    public class HelloReply
    {
        [ProtoMember(1)]
        public string Message { get; set; }
    }

    static class Consumer
    {
        static async Task TheirCode()
        {
            var channel = new Channel("localhost:50051", ChannelCredentials.Insecure);

            var client = ClientFactory.CreateClient<IMyService>(channel);

            HelloReply response = await client.SayHelloAsync(new HelloRequest { Name = "abc" });
            Console.WriteLine(response.Message);

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

    public static class ClientFactory
    {
        public static T CreateClient<T>(Channel channel) where T : class
        {
            ClientBase client = default;
            if (typeof(T) == typeof(IGreeter))
            {
                client = new GreeterClient(channel);
            }
            return (T)(object)(client);
        }
    }
    //public readonly struct ClientProxy<T> : IDisposable
    //    where T : class
    //{
    //    private readonly ClientBase _client;

    //    internal ClientProxy(ClientBase client) => _client = client;


    //    public T Channel
    //    {
    //        // assume default behaviour is for the client to implement it directly, but allow alternatives
    //        [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //        get => (T)(object)_client;
    //    }

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public void Dispose() => (_client as IDisposable)?.Dispose();

    //    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    //    public static implicit operator T (ClientProxy<T> proxy) => proxy.Channel;
    //}

    public interface IGreeter
    {
        // this is the inital version that assumes same client API as google
        AsyncUnaryCall<HelloReply> SayHello(HelloRequest request, CallOptions options = default);
        AsyncServerStreamingCall<HelloReply> SayHellos(HelloRequest request, CallOptions options = default);
    }

    // this is approximately what we want to emit
    internal sealed class GreeterClient : ClientBase, IGreeter
    {
        internal GreeterClient(Channel channel) : base(channel) { }

        private const string SERVICE_NAME = "Greet.Greeter";
        public override string ToString() => SERVICE_NAME;

        AsyncUnaryCall<HelloReply> IGreeter.SayHello(HelloRequest request, CallOptions options)
            => CallInvoker.AsyncUnaryCall(s_SayHelloAsync, null, options, request);

        AsyncServerStreamingCall<HelloReply> IGreeter.SayHellos(HelloRequest request, CallOptions options)
            => CallInvoker.AsyncServerStreamingCall(s_SayHellosAsync, null, options, request);

        static readonly Method<HelloRequest, HelloReply> s_SayHelloAsync = new FullyNamedMethod<HelloRequest, HelloReply>(
            "SayHello", MethodType.Unary, SERVICE_NAME, nameof(IGreeter.SayHello));

        static readonly Method<HelloRequest, HelloReply> s_SayHellosAsync = new FullyNamedMethod<HelloRequest, HelloReply>(
            "SayHellos", MethodType.ServerStreaming, SERVICE_NAME, nameof(IGreeter.SayHellos));
    }
}


