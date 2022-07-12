# Grpc.AspNetCore.Web

Grpc.AspNetCore.Web provides middleware that enables ASP.NET Core gRPC services to accept gRPC-Web calls.

In *Startup.cs*:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddGrpc();
    services.AddGrpcWeb(o => o.GrpcWebEnabled = true);
}

public void Configure(IApplicationBuilder app)
{
    app.UseRouting();
    app.UseGrpcWeb();
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapGrpcService<GreeterService>();
    });
}
```

gRPC-Web can be enabled for all gRPC services by setting `GrpcWebOptions.GrpcWebEnabled = true`, or enabled on individual services with `EnableGrpcWeb()`:

```csharp
app.UseEndpoints(endpoints =>
{
    endpoints.MapGrpcService<GreeterService>().EnableGrpcWeb();
});
```

### gRPC-Web and streaming

Traditional gRPC over HTTP/2 supports streaming in all directions. gRPC-Web offers limited support for streaming:

* gRPC-Web browser clients don't support calling client streaming and bidirectional streaming methods.
* gRPC-Web .NET clients don't support calling client streaming and bidirectional streaming methods over HTTP/1.1.
* ASP.NET Core gRPC services hosted on Azure App Service and IIS don't support bidirectional streaming.

When using gRPC-Web, we only recommend the use of unary methods and server streaming methods.

## Links

* [Documentation](https://docs.microsoft.com/aspnet/core/grpc/browser)
* [grpc-dotnet GitHub](https://github.com/grpc/grpc-dotnet)
