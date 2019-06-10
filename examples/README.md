# gRPC for .NET Examples

Examples of basic gRPC scenarios with gRPC for .NET. Each scenario has its own client. A shared website hosts all services.

## Greeter

The greeter shows how to make unary and server streaming gRPC methods and call them from a client.

## Counter

The counter shows how to make unary and client streaming gRPC methods and call them from a client.

## Mailer

The mailer shows how to make bi-directional streaming gRPC methods and call them from a client.

## Ticketer

The ticketer shows how to use gRPC with [authorization in ASP.NET Core](https://docs.microsoft.com/aspnet/core/security/authorization/introduction). This example has a gRPC method marked with an `[Authorize]` attribute. The client can only call the method if it has been authenticated by the server and passes a valid JWT token with the gRPC call.

## Reflector

The reflector shows how to host the [gRPC Server Reflection Protocol](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md) service and call its methods from a client.

## Certifier

The certifier shows how to configure the client and the server to use a client certificate with a gRPC call.

## Worker

The worker shows how a [worker service](https://devblogs.microsoft.com/aspnet/net-core-workers-as-windows-services/) can use the gRPC client factory to make gRPC calls.
