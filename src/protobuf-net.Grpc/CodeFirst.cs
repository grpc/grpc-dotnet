// this is just me thinking aloud; "how would someone expect to use this?"

using Grpc.Core;
using ProtoBuf;
using System;
using System.Reflection;
using System.Reflection.Emit;
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
            if (typeof(T) == typeof(IGreeter))
            {
                ClientBase client = new GreeterClient(channel);
                return (T)(object)(client);
            }
            return ProxyCache<T>.Create(channel);
        }
    }

    static class ProxyEmitter
    {
        private const string ProxyIdentity = "ProtoBufNetGeneratedProxies";

        static ProxyEmitter()
        {
            AssemblyBuilder asm = AssemblyBuilder.DefineDynamicAssembly(
                new AssemblyName(ProxyIdentity), AssemblyBuilderAccess.Run);
            _module = asm.DefineDynamicModule(ProxyIdentity);
        }
        static readonly ModuleBuilder _module;

        internal static Func<Channel, TService> CreateFactory<TService>()
            where TService : class
        {
            lock (_module)
            {
                // private sealed clas FooProxy : ClientBase
                var type = _module.DefineType(ProxyIdentity + "." + typeof(TService).Name + "Proxy",
                    TypeAttributes.Class | TypeAttributes.Sealed | TypeAttributes.NotPublic,
                    parent: typeof(ClientBase));

                // : TService
                type.AddInterfaceImplementation(typeof(TService));

                // public FooProxy(Channel channel)
                var ctor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, s_ctorSignature);
                var il = ctor.GetILGenerator();

                // => base(channel) {}
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, s_baseCtor);
                il.Emit(OpCodes.Ret);

            }

            throw new NotImplementedException();
        }
        static readonly Type[] s_ctorSignature = new Type[] { typeof(Channel) };
        static readonly ConstructorInfo s_baseCtor = typeof(ClientBase)
            .GetConstructor(s_ctorSignature);
    }

    internal static class ProxyCache<TService>
        where TService : class
    {
        public static TService Create(Channel channel) => s_ctor(channel);
        private static readonly Func<Channel, TService> s_ctor = ProxyEmitter.CreateFactory<TService>();
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


