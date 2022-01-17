# Grpc.Net.ClientFactory

gRPC integration with `HttpClientFactory` offers a centralized way to create gRPC clients. It can be used as an alternative to [configuring stand-alone gRPC client instances](https://docs.microsoft.com/aspnet/core/grpc/client).

The factory offers the following benefits:

* Provides a central location for configuring logical gRPC client instances
* Manages the lifetime of the underlying `HttpClientMessageHandler`
* Automatic propagation of deadline and cancellation in an ASP.NET Core gRPC service

## Register gRPC clients

To register a gRPC client, the generic `AddGrpcClient` extension method can be used within `Startup.ConfigureServices`, specifying the gRPC typed client class and service address:

```csharp
services.AddGrpcClient<Greeter.GreeterClient>(o =>
{
    o.Address = new Uri("https://localhost:5001");
});
```

The gRPC client type is registered as transient with dependency injection (DI). The client can now be injected and consumed directly in types created by DI. ASP.NET Core MVC controllers, SignalR hubs and gRPC services are places where gRPC clients can automatically be injected:

```csharp
public class AggregatorService : Aggregator.AggregatorBase
{
    private readonly Greeter.GreeterClient _client;

    public AggregatorService(Greeter.GreeterClient client)
    {
        _client = client;
    }

    public override async Task SayHellos(HelloRequest request,
        IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
    {
        // Forward the call on to the greeter service
        using (var call = _client.SayHellos(request))
        {
            await foreach (var response in call.ResponseStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(response);
            }
        }
    }
}
```

## Links

* [Documentation](https://docs.microsoft.com/aspnet/core/grpc/clientfactory)
* [grpc-dotnet GitHub](https://github.com/grpc/grpc-dotnet)
