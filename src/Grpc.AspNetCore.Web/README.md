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

## Links

* [Documentation](https://docs.microsoft.com/aspnet/core/grpc/browser)
* [grpc-dotnet GitHub](https://github.com/grpc/grpc-dotnet)
