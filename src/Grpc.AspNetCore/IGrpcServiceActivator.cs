namespace GRPCServer.Dotnet
{
    interface IGrpcServiceActivator<TGrpcService> where TGrpcService : class
    {
        TGrpcService Create();
        void Release(TGrpcService service);
    }
}
