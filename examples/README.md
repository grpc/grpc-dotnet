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

> **NOTE:** client.pfx is a self-signed certificate. When running the client you may get an error that the certificate is not trusted: `The certificate chain was issued by an authority that is not trusted`. [Add the certificate](https://www.thesslstore.com/knowledgebase/ssl-install/how-to-import-intermediate-root-certificates-using-mmc/) to your computer's trusted root cert store to fix this error. Don't use this certificate in production environments.

##### Scenarios:

* Client certificate authentication
* Send client certificate with call
* Receive client certificate in a service
* Authorization with `[Authorize]` on service

## [Worker](./Worker)

The worker shows how to use call a gRPC server with a [.NET worker service](https://docs.microsoft.com/aspnet/core/fundamentals/host/hosted-services). The client uses the worker service to make a gRPC call on a timed internal. The gRPC client factory is used to create a client, which is injected into the service using dependency injection.

The server is configured as a normal .NET web app, which uses the same [generic host](https://docs.microsoft.com/aspnet/core/fundamentals/host/generic-host) as a worker service to host its web server.

The client or server can be run as a [Windows service](https://en.wikipedia.org/wiki/Windows_service) or [systemd service](https://www.freedesktop.org/wiki/Software/systemd/) with some minor changes to the project file and startup logic:

* [.NET Core Workers as Windows Services](https://devblogs.microsoft.com/aspnet/net-core-workers-as-windows-services/)
* [.NET Core and systemd](https://devblogs.microsoft.com/dotnet/net-core-and-systemd/)

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

The tester shows how to test gRPC services. The unit tests create and test a gRPC service directly. The functional tests show how to use [Microsoft.AspNetCore.TestHost](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost/) to host a gRPC service with an in-memory test server and call it using a gRPC client. The functional tests write client and server logs to the test output.

> **NOTE:** There is a known issue in ASP.NET Core 3.0 that prevents functional testing of bidirectional gRPC methods. Bidirectional gRPC methods can still be unit tested.

##### Scenarios:

* Unit testing
* Functional testing

## [Progressor](./Progressor)

The progressor shows how to use server streaming to notify the caller about progress on the server.

##### Scenarios:

* Server streaming
* Using [`Progress<T>`](https://docs.microsoft.com/en-us/dotnet/api/system.progress-1) to notify progress on the client

## [Vigor](./Vigor)

The vigor example shows how to integrate [ASP.NET Core health checks](https://docs.microsoft.com/aspnet/core/host-and-deploy/health-checks) with the [gRPC Health Checking Protocol](https://github.com/grpc/grpc/blob/master/doc/health-checking.md) service, and call its methods from a client.

##### Scenarios:

* Hosting gRPC Health Checking Protocol service
* Integrate [ASP.NET Core health checks](https://docs.microsoft.com/aspnet/core/host-and-deploy/health-checks) with gRPC health checks
* Calling service with `Grpc.HealthCheck` client
