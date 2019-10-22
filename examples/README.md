# gRPC for .NET Examples

Examples of basic gRPC scenarios with gRPC for .NET.

If you are brand new to gRPC on .NET a good place to start is the getting started tutorial: [Create a gRPC client and server in ASP.NET Core](https://docs.microsoft.com/aspnet/core/tutorials/grpc/grpc-start)

## [Greeter](./Greeter)

The greeter shows how to create unary (non-streaming) and server streaming gRPC methods in ASP.NET Core, and call them from a client.

##### Scenarios:

* Unary call
* Server streaming call
* Client canceling a call

## [Counter](./Counter)

The counter shows how to create unary (non-streaming) and client streaming gRPC methods in ASP.NET Core, and call them from a client.

##### Scenarios:

* Unary call
* Client streaming call

## [Mailer](./Mailer)

The mailer shows how to create a bi-directional streaming gRPC method in ASP.NET Core and call it from a client. The server reacts to messages sent from the client.

##### Scenarios:

* Bi-directional streaming call

## [Logger](./Logger)

The logger shows how to use interceptors on the client and server. The client interceptor adds additional metadata to each call and the server interceptor logs that metadata on the server.

##### Scenarios:

* Creating a client interceptor
* Using a client interceptor
* Creating a server interceptor
* Using a server interceptor

## [Racer](./Racer)

The racer shows how to create a bi-directional streaming gRPC method in ASP.NET Core and call it from a client. The client and the server each send messages as quickly as possible.

##### Scenarios:

* Bi-directional streaming call

## [Ticketer](./Ticketer)

The ticketer shows how to use gRPC with [authentication and authorization in ASP.NET Core](https://docs.microsoft.com/aspnet/core/security). This example has a gRPC method marked with an `[Authorize]` attribute. The client can only call the method if it has been authenticated by the server and passes a valid JWT token with the gRPC call.

##### Scenarios:

* JSON web token authentication
* Send JWT token with call
* Authorization with `[Authorize]` on service

## [Reflector](./Reflector)

The reflector shows how to host the [gRPC Server Reflection Protocol](https://github.com/grpc/grpc/blob/master/doc/server-reflection.md) service and call its methods from a client.

##### Scenarios:

* Hosting gRPC Server Reflection Protocol service
* Calling service with `Grpc.Reflection` client

## [Certifier](./Certifier)

The certifier shows how to configure the client and the server to use a [TLS client certificate](https://blogs.msdn.microsoft.com/kaushal/2015/05/27/client-certificate-authentication-part-1/) with a gRPC call. The server is configured to require a client certificate using [ASP.NET Core client certificate authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/certauth).

##### Scenarios:

* Client certificate authentication
* Send client certificate with call
* Receive client certificate in a service
* Authorization with `[Authorize]` on service

## [Worker](./Worker)

The worker shows how a [.NET worker service](https://devblogs.microsoft.com/aspnet/net-core-workers-as-windows-services/) can use the gRPC client factory to make gRPC calls.

##### Scenarios:

* Worker service
* Client factory

## [Aggregator](./Aggregator)

The aggregator shows how a to make nested gRPC calls (a gRPC service calling another gRPC service). The gRPC client factory is used in ASP.NET Core to inject a client into services. The gRPC client factory is configured to propagate the context from the original call to the nested call. In this example the cancellation from the client will automatically propagate through to nested gRPC calls.

The aggregator can optionally be run with [OpenTelemetry](https://github.com/open-telemetry/opentelemetry-dotnet) enabled. OpenTelemetry is configured to capture tracing information and send it to [Zipkin](https://zipkin.io), a distributed tracing system. A Zipkin server needs to be running to receive traces. The simplest way to do that is [run the Zipkin Docker image](https://zipkin.io/pages/quickstart.html).

To run the aggregator server with OpenTelemetry enabled:

```console
dotnet run --EnableOpenTelemetry=true
```

##### Scenarios:

* Client factory
* Client canceling a call
* Cancellation propagation
* Capture tracing with [OpenTelemetry](https://github.com/open-telemetry/opentelemetry-dotnet) (optional)

## [Tester](./Tester)

The tester shows how to test gRPC services. The unit tests create and test a gRPC service directly. The functional tests show how to use [Microsoft.AspNetCore.TestHost](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost/) to host a gRPC service with an in-memory test server and call it using a gRPC client.

##### Scenarios:

* Unit testing
* Functional testing