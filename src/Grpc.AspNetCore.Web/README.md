# Experimental project

This is an experimental project. If you have issues or suggestions for features, please give feedback at https://github.com/grpc/grpc-dotnet/issues

---

[gRPC-Web](https://grpc.io/blog/state-of-grpc-web/) allows gRPC to be used from browser applications. gRPC-Web enables using gRPC in new scenarios:

- JavaScript browser applications can call gRPC services using the [gRPC-Web JavaScript client](https://github.com/grpc/grpc-web).
- Blazor WebAssembly applications can call gRPC services using the .NET Core gRPC client.
- gRPC services can be used in environments that don't have complete support for HTTP/2.
- gRPC can be used with technologies not available in HTTP/2, e.g. Windows authentication.

Grpc.AspNetCore.Web and Grpc.Net.Client.Web provide extensions to enable end-to-end gRPC-Web support for .NET Core.

## Grpc.AspNetCore.Web

Grpc.AspNetCore.Web provides middleware that enables ASP.NET Core gRPC services to accept gRPC-Web calls.

*Startup.cs*

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

## Grpc.Net.Client.Web

Grpc.Net.Client.Web provides a HttpClient delegating handler that configures the .NET Core gRPC client to send gRPC-Web calls.

```csharp
// Create channel
var handler = ew GrpcWebHandler(GrpcWebMode.GrpcWebText, new HttpClientHandler());
var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
    {
        HttpClient = new HttpClient(handler)
    });

// Make call with a client
var client = Greeter.GreeterClient(channel);
var response = await client.SayHelloAsync(new GreeterRequest { Name = ".NET" });
```