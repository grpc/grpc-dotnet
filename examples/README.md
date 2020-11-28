# gRPC for .NET Examples

Examples of basic gRPC scenarios with gRPC for .NET.

If you are brand new to gRPC on .NET a good place to start is the getting started tutorial: [Create a gRPC client and server in ASP.NET Core](https://docs.microsoft.com/aspnet/core/tutorials/grpc/grpc-start)

## [Greeter](./Greeter)

The greeter shows how to create unary (non-streaming) gRPC methods in ASP.NET Core, and call them from a client.

##### Scenarios:

* Unary call

## [Counter](./Counter)

The counter shows how to create unary (non-streaming), client streaming and server streaming gRPC methods in ASP.NET Core, and call them from a client.

##### Scenarios:

* Unary call
* Client streaming call
* Server streaming call

## [Mailer](./Mailer)

The mailer shows how to create a bi-directional streaming gRPC method in ASP.NET Core and call it from a client. The server reacts to messages sent from the client.

##### Scenarios:

* Bi-directional streaming call

## [Interceptor](./Interceptor)

The interceptor shows how to use gRPC interceptors on the client and server. The client interceptor adds additional metadata to each call and the server interceptor logs that metadata on the server.

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

The tester shows how to test gRPC services. The unit tests create and test a gRPC service directly. The functional tests show how to use [Microsoft.AspNetCore.TestHost](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost/) (version 3.1.2 or greater required) to host a gRPC service with an in-memory test server and call it using a gRPC client. The functional tests write client and server logs to the test output.

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

## [Compressor](./Compressor)

The compressor example shows how to enable compression of gRPC request and response messages using gzip.

> **IMPORTANT:** Using compression with dynamically generated content can lead to security problems such as the [CRIME](https://wikipedia.org/wiki/CRIME_(security_exploit)) and [BREACH](https://wikipedia.org/wiki/BREACH_(security_exploit)) attacks.

##### Scenarios:

* Compression of request messages. gRPC clients should use the `grpc-internal-encoding-request` metadata value.
* Compression of response messages. gRPC services should configure the `ResponseCompressionAlgorithm` setting.

## [Liber](./Liber)

The liber example shows how to add Protobuf messages to a shared .NET project. Sharing generated messages is an alternative to each project generating their own copy. Protobuf messages in a shared project makes it easier to write reusable libraries that use messages.

This example has two proto files:

* *common.proto* contains a common `Name` message type.
* *greet.proto* has a service definition. It imports *common.proto* and uses the `Name` message.

The `Name` .NET type is generated from *common.proto* in the common project and is shared throughout the solution:

* *Common.csproj* uses Grpc.Tools to generate messages contained in *common.proto*.
* *Client.csproj* uses Grpc.Tools to generate the gRPC client for *greet.proto*. There is no `<Protobuf>` reference for *common.proto* because we don't want its messages generated in this project. Instead the .NET types for its messages are referenced from the common project.
* *Server.csproj* uses Grpc.Tools to generate the gRPC service for *greet.proto*. It also references the common project.

##### Scenarios:

* Add Protobuf messages to shared .NET projects
* Use shared messages in gRPC services

## [Browser](./Browser)

The browser example shows how to use [gRPC-Web](https://github.com/grpc/grpc-web) with ASP.NET Core to call a gRPC service from a browser. Browser apps have limited HTTP/2 features and need to use gRPC-Web instead. This example requires [npm and NodeJS](https://nodejs.org/) to be installed on your computer.

The gRPC-Web JavaScript client was generated from *greet.proto* using [`protoc`](https://github.com/protocolbuffers/protobuf/releases) with the [`protoc-gen-grpc-web`](https://github.com/grpc/grpc-web/releases) plugin.

##### Scenarios:

* Configure ASP.NET Core server to support `grpc-web` and `grpc-web-text` content types
* Call gRPC services with JavaScript from a browser

## [Blazor](./Blazor)

The blazor example shows how to call a gRPC service from a Blazor WebAssembly app. Because Blazor WebAssembly is hosted in the browser it has limited HTTP/2 features and needs to use gRPC-Web instead.

##### Scenarios:

* Configure ASP.NET Core server to support `grpc-web` and `grpc-web-text` content types
* Configure .NET gRPC client in Blazor to use gRPC-Web
* Get service address using `IConfiguration` and `appsettings.json`
* Call gRPC services with Blazor WebAssembly from a browser

## [Spar](./Spar)

The spar example shows how to call a gRPC service from a single page application (SPA) and make cross-origin gRPC-Web requests. The SPA uses [Vue.js](https://vuejs.org/) and [gRPC-Web](https://github.com/grpc/grpc-web). The server is configured to support [Cross Origin Resource Sharing (CORS)](https://docs.microsoft.com/aspnet/core/security/cors). This example requires [npm and NodeJS](https://nodejs.org/) to be installed on your computer.

The gRPC-Web JavaScript client was generated from *greet.proto* using [`protoc`](https://github.com/protocolbuffers/protobuf/releases) with the [`protoc-gen-grpc-web`](https://github.com/grpc/grpc-web/releases) plugin.

##### Scenarios:

* Configure ASP.NET Core server to support `grpc-web` and `grpc-web-text` content types
* Configure ASP.NET Core server to enable gRPC-Web cross-origin requests (CORS)
* Call gRPC services with JavaScript from a SPA

## [Microservicer](./Microservicer)

The microservicer example shows how to use gRPC in a solution that contains multiple web apps. The backend app hosts a gRPC service that is called by the frontend app. The frontend app uses gRPC client factory to create a client an inject it into the MVC controller with dependency injection.

This example uses [tye](https://github.com/dotnet/tye) to run the solution and for service discovery. When `tye run` command is run:

1. Tye builds and runs the frontend and backend apps.
2. Tye injects service addresses into each app. The backend app address is accessible via `IConfiguration`.
3. The frontend app configures the gRPC client with the backend address in *Startup.cs*.

[Install the Tye dotnet tool](https://github.com/dotnet/tye/blob/master/docs/getting_started.md) and then execute `tye run` in the *examples/Microservicer* directory.

##### Scenarios:

* Microservices
* Client factory
* Mixing frontend RESTful API with backend gRPC services
* App development with [Tye](https://github.com/dotnet/tye)

## [Transporter](./Transporter)

**Requirements:**
* .NET 5 or later
* Linux, MacOS or a [modern version of Windows](https://devblogs.microsoft.com/commandline/af_unix-comes-to-windows/)

The transporter example shows how to use gRPC over non-TCP transports. This example uses a [Unix domain socket (UDS)](https://en.wikipedia.org/wiki/Unix_domain_socket) to send gRPC messages between the client and server.

To use gRPC with UDS:

1. The client creates a channel with a `ConnectCallback`. The callback connects to a specified UDS endpoint.
2. The server configures a UDS endpoint with [KestrelServerOptions.ListenUnixSocket](https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserveroptions.listenunixsocket) in *Program.cs*.

##### Scenarios:

* Unix domain sockets
* SocketsHttpHandler.ConnectCallback
* [KestrelServerOptions.ListenUnixSocket](https://docs.microsoft.com/dotnet/api/microsoft.aspnetcore.server.kestrel.core.kestrelserveroptions.listenunixsocket)
