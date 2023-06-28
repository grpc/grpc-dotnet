# Grpc.AspNetCore.Web

Grpc.AspNetCore.Web provides middleware that enables ASP.NET Core gRPC services to accept gRPC-Web calls.

## Configure gRPC-Web

In *Program.cs*:

```csharp
using GrpcGreeter.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseRouting();
app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
app.MapGrpcService<GreeterService>();

app.Run();
```

gRPC-Web can be enabled for all gRPC services by setting `GrpcWebOptions.DefaultEnabled = true`, or enabled on individual services with `EnableGrpcWeb()`:

```csharp
app.MapGrpcService<GreeterService>().EnableGrpcWeb();
```

### gRPC-Web and streaming

Traditional gRPC over HTTP/2 supports streaming in all directions. gRPC-Web offers limited support for streaming:

* gRPC-Web browser clients don't support calling client streaming and bidirectional streaming methods.
* gRPC-Web .NET clients don't support calling client streaming and bidirectional streaming methods over HTTP/1.1.
* ASP.NET Core gRPC services hosted on Azure App Service and IIS don't support bidirectional streaming.

When using gRPC-Web, we only recommend the use of unary methods and server streaming methods.

## Links

* [Documentation](https://learn.microsoft.com/aspnet/core/grpc/browser)
* [grpc-dotnet GitHub](https://github.com/grpc/grpc-dotnet)
