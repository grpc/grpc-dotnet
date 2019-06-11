# gRPC for .NET Examples

Examples of basic gRPC scenarios with gRPC for .NET. Each scenario has its own client. A shared website hosts all services.

* [Clients](./Clients)
* [Server](./Server)

## Greeter

The greeter shows how to make unary and server streaming gRPC methods and call them from a client.

* Unary
* Server streaming
* Cancellation

## Counter

The counter shows how to make unary and client streaming gRPC methods and call them from a client.

* Unary
* Client streaming

## Mailer

The mailer shows how to make bi-directional streaming gRPC methods and call them from a client.

* Bi-directional streaming

## Ticketer

The ticketer shows how to use gRPC with [authorization in ASP.NET Core](https://docs.microsoft.com/aspnet/core/security/authorization/introduction). This example has a gRPC method marked with an `[Authorize]` attribute. The client can only call the method if it has been authenticated by the server and passes a valid JWT token with the gRPC call.

* JSON web token authentication
* Authorization

## Reflector

The reflector shows how to host the [gRPC Server Reflection Protocol](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md) service and call its methods from a client.

* gRPC Server Reflection Protocol

## Certifier

The certifier shows how to configure the client and the server to use a client certificate with a gRPC call.

* Client certificate

## Worker

The worker shows how a [worker service](https://devblogs.microsoft.com/aspnet/net-core-workers-as-windows-services/) can use the gRPC client factory to make gRPC calls.

* Worker service
* Client factory

## Aggregator

The aggregator shows how a to use the gRPC client factory with services to forward calls. The gRPC client factory is configured to propagate the context from the original call to the forwarded call. For example, cancellation will automatically propagate.

* Client factory
* Cancellation
* Context propagation