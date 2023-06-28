# Grpc.AspNetCore

`Grpc.AspNetCore` is a metapackage with references to:

* `Grpc.AspNetCore.Server`: gRPC server library for .NET.
* `Grpc.Tools`: Code-generation tooling package.
* `Google.Protobuf`: Protobuf serialization library.

## Configure gRPC

In *Program.cs*:

* gRPC is enabled with the `AddGrpc` method.
* Each gRPC service is added to the routing pipeline through the `MapGrpcService` method. For information about how to create gRPC services, see [Create gRPC services and methods](https://learn.microsoft.com/aspnet/core/grpc/services).

```csharp
using GrpcGreeter.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddGrpc();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGrpcService<GreeterService>();

app.Run();
```

ASP.NET Core middleware and features share the routing pipeline, therefore an app can be configured to serve additional request handlers. The additional request handlers, such as MVC controllers, work in parallel with the configured gRPC services.

## Links

* [Documentation](https://learn.microsoft.com/aspnet/core/grpc/aspnetcore)
* [grpc-dotnet GitHub](https://github.com/grpc/grpc-dotnet)
