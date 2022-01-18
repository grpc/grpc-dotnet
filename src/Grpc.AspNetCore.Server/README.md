# Grpc.AspNetCore.Server

`Grpc.AspNetCore.Server` is a gRPC server library for .NET.

## Configure gRPC

In *Startup.cs*:

* gRPC is enabled with the `AddGrpc` method.
* Each gRPC service is added to the routing pipeline through the `MapGrpcService` method. For information about how to create gRPC services, see [Create gRPC services and methods](https://docs.microsoft.com/aspnet/core/grpc/services).

```csharp
public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddGrpc();
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGrpcService<GreeterService>();
        });
    }
}
```

ASP.NET Core middleware and features share the routing pipeline, therefore an app can be configured to serve additional request handlers. The additional request handlers, such as MVC controllers, work in parallel with the configured gRPC services.

## Links

* [Documentation](https://docs.microsoft.com/aspnet/core/grpc/aspnetcore)
* [grpc-dotnet GitHub](https://github.com/grpc/grpc-dotnet)
