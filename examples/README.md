# gRPC for .NET Examples

Examples of basic gRPC scenarios with gRPC for .NET.

* [Clients](./Clients) - each example has its own client
* [Server](./Server) - a shared website that hosts all services

## Greeter

The greeter shows how to make unary and server streaming gRPC methods and call them from a client.

##### Scenarios:

* Unary call
* Server streaming call
* Client canceling a streaming call

## Counter

The counter shows how to make unary and client streaming gRPC methods and call them from a client.

##### Scenarios:

* Unary call
* Client streaming call

## Mailer

The mailer shows how to make bi-directional streaming gRPC methods and call them from a client.

##### Scenarios:

* Bi-directional streaming call

## Ticketer

The ticketer shows how to use gRPC with [authorization in ASP.NET Core](https://docs.microsoft.com/aspnet/core/security/authorization/introduction). This example has a gRPC method marked with an `[Authorize]` attribute. The client can only call the method if it has been authenticated by the server and passes a valid JWT token with the gRPC call.

##### Scenarios:

* JSON web token authentication
* Authorization with attribute on service

## Reflector

The reflector shows how to host the [gRPC Server Reflection Protocol](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md) service and call its methods from a client.

##### Scenarios:

* Hosting gRPC Server Reflection Protocol service
* Calling service with `Grpc.Reflection` client

## Certifier

The certifier shows how to configure the client and the server to use a [TLS client certificate](https://blogs.msdn.microsoft.com/kaushal/2015/05/27/client-certificate-authentication-part-1/) with a gRPC call.

##### Scenarios:

* Send client certificate with call
* Receive client certificate in a service

## Worker

The worker shows how a [worker service](https://devblogs.microsoft.com/aspnet/net-core-workers-as-windows-services/) can use the gRPC client factory to make gRPC calls.

##### Scenarios:

* Worker service
* Client factory

## Aggregator

The aggregator shows how a to use the gRPC client factory with services to make nested gRPC calls. The gRPC client factory is configured to propagate the context from the original call to the nested call. For example, cancellation will automatically propagate.

##### Scenarios:

* Client factory
* Client canceling a streaming call
* Context propagation