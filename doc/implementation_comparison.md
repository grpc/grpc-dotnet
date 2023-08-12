# grpc-dotnet and Grpc.Core comparison

This document summarizes the differences between the two available implementations of gRPC in C#.

## Where both implementations are the same

- 100% wire compatible and interoperable with each other and with other gRPC implementations
- same API for invoking and handling RPC calls. Note that the way server and client are configured at the startup is [different](https://docs.microsoft.com/en-us/aspnet/core/grpc/migration?view=aspnetcore-3.0)
- basic functionality (streaming, metadata, deadline, cancellation, ...)
- using same codegen tooling and MSBuild Integration (Grpc.Tools)

## Criteria for choosing between grpc-dotnet and gRPC C#

Starting from May 2021, gRPC for .NET is the recommended implemention of gRPC for C#.
The original [gRPC C#](https://github.com/grpc/grpc/tree/master/src/csharp) implementation (distributed as the `Grpc.Core` nuget package) is now in maintenance mode and will be deprecated in the future.
See [blogpost](https://grpc.io/blog/grpc-csharp-future/) for more details.

Here are some key points in which the two implementation differ:

- grpc-dotnet avoids the use of native code (while Grpc.Core use the native C-core library internally)
- grpc-dotnet requires a newer version of .NET (see the "Framework supported" section)
- grpc-dotnet server integrates seamlessly ASP.NET Core (and allows e.g. dependency injection)
- performance (while data we have data that seems to indicate that grpc-dotnet peforms at least as well as Grpc.Core, we strongly encourage to run your own benchmarks if performance matters for your application)
- features available (see breakdown below)

## Frameworks supported

Grpc.Core supports a wide range of .NET versions, including some very old ones. A more detailed overview is [here]( https://github.com/grpc/grpc/tree/master/src/csharp#supported-platforms)

grpc-dotnet uses features only available in modern .NET releases. It doesn't support some older versions of .NET. A detailed summary of .NET versions supported by grpc-dotnet is [here](https://docs.microsoft.com/aspnet/core/grpc/supported-platforms). There is limited support for [grpc-dotnet client support on
legacy .NET Framework](https://docs.microsoft.com/aspnet/core/grpc/netstandard).

## Comparison of supported features 

Beyond the basic RPC functionality, there are a lot of gRPC features that may or may not be supported. The summary
of supported features in both implementation is available in this section.

### Proxyless service mesh (XDS) support

While support for some of the Proxyless service mesh functionality comes "for free" by virtue of using the implementation from C-core native library, we don't officially support the proxyless service mesh functionality in C#.

In grpc-dotnet, we currently don't provide proxyless service mesh support, but it's something that we plan to add in the future. One of the first features we want to integrate is XDS load balancing.

### Load Balancing

grpc-dotnet and Grpc.Core provides basic client load balancing policies PICK_FIRST, ROUND_ROBIN.

Grpc.Core also has implemented two client-lookaside LB policies, but we don't recommend using them:

- grpclb - limited use externally as there's no official implementation of the LB policy. We don't recommend using it as it's been deprecated by the XDS load balancing.
- XDS - Load balancing using the Envoy Universal Data Plane APIs (xDS). It does work in Grpc.Core (because it's implemented in C-core native library), but as noted above, we don't provide official support for the proxyless service mesh functionality in Grpc.Core.

Proxy load balancing is supported by both implementations because load balancing is done by a separate process (e.g. Envoy, ngingx etc.) that proxies the traffic.

grpc-dotnet allows user-provided custom load-balancing policies (= a plugin that provides the load-balancing logic).

Also see:

- https://learn.microsoft.com/aspnet/core/grpc/loadbalancing
- https://github.com/grpc/grpc/blob/master/doc/load-balancing.md
- https://github.com/grpc/grpc/blob/master/doc/naming.md
- https://github.com/grpc/proposal/blob/master/A5-grpclb-in-dns.md
- https://github.com/grpc/proposal/blob/master/A24-lb-policy-config.md

### Service config

Service config is supported by grpc-dotnet and Grpc.Core. Right now, the feature is not that useful 
because support for service config encoded in DNS records hasn't been enabled yet by default.

Also see:

- https://github.com/grpc/grpc/blob/master/doc/service_config.md
- https://github.com/grpc/proposal/blob/master/A2-service-configs-in-dns.md 

### Tracing

grpc-dotnet has tracing bindings that come from ASP.NET Core, and it works with OpenTelemetry SDK, OpenCensus support is TBD. See [OpenTelemetry Example](https://github.com/grpc/grpc-dotnet/tree/master/examples#aggregator).

Grpc.Core: currently not supported.

### Interceptors

Both implementations support client and server interceptors from Grpc.Core.Interceptors namespace. Interceptors operate at post-deserialization and pre-serialization level (no access to binary payloads).

In addition to gRPC-aware interceptors, grpc-dotnet also allows interception at an HTTP/2 level:

- Incoming gRPC HTTP/2 requests can be processed using [ASP.NET Core middleware](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/middleware/).
- Outgoing gRPC HTTP/2 requests can be processed using [HttpClient HttpMessageHandlers](https://docs.microsoft.com/en-us/dotnet/api/system.net.http.httpmessagehandler).

### Transport support

Both implementations fully support gRPC over HTTP/2 in both TLS and plaintext.

Grpc.Core gets support for other transports supported by C-Core for free. Some (minor) integration work might be required to actually use these transports with gRPC C#.

grpc-dotnet supports other transports in .NET 5 or later. For example, grpc-dotnet [supports interprocess communication (IPC)](https://learn.microsoft.com/aspnet/core/grpc/interprocess) using named pipes and Unix domain sockets.

Notes:

- Grpc.Core allows connections over UDS socket (both server and client) on Unix systems. It doesn't support named pipes on windows.
- Grpc.Core supports additional "transports" like ALTS and cfstream thanks to being build on top of C-core.
- Grpc.Core could provide an inprocess transport support but currently this functionality is not exposed in C# API.
- grpc-dotnet support for TLS is platform dependent. TLS is fully supported on Windows and Linux. There is limited support for servers hosted on MacOS prior to .NET 8.

### Retries / Request Hedging

Grpc.Core: implementation in C-core in progress, but no ETA yet

grpc-dotnet: Retries and hedging are fully supported.

Also see:

- https://learn.microsoft.com/aspnet/core/grpc/retries
- https://github.com/grpc/proposal/blob/master/A6-client-retries.md

### Channelz

Grpc.Core: not supported at the moment, but most of functionality is available in C-core through an API, a C# gRPC service needs to be implemented to expose the stats obtained from C-core as a gRPC service (relatively easy)

grpc-dotnet: not supported

https://github.com/grpc/proposal/blob/master/A14-channelz.md

### Binary logging

Grpc.Core: Implemented in C-core, but not exposed in the C# layer.

grpc-dotnet: not implemented

https://github.com/grpc/grpc/blob/master/doc/binary-logging.md

### Compression

Grpc.Core: supported (algorithms: `gzip`, `deflate`)

grpc-dotnet: supported (algorithms: `gzip`, `deflate`), also provides public API to provide custom compression algorithm.

Performance implications of using compression in both implementations haven't been measured. Compression functionality is offered mostly to comply with the spec.

https://github.com/grpc/grpc/blob/master/doc/compression.md
https://github.com/grpc/grpc/blob/master/doc/compression_cookbook.md

### Fine-grained Transport Control: Connectivity API

Grpc.Core: supported (exposed publicly on Channel), provided by C-Core

grpc-dotnet: Supported on .NET 5 or later

https://github.com/grpc/grpc/blob/master/doc/connectivity-semantics-and-api.md

### Fine-grained Transport Control: Connection Backoff

Grpc.Core: supported, provided by C-core

grpc-dotnet: not supported (HttpClient doesn't support)

https://github.com/grpc/grpc/blob/master/doc/connection-backoff.md

### Fine-grained Transport Control: Keepalive

Grpc.Core: supported, provided by C-core

grpc-dotnet: not supported (HttpClient and Kestrel don't provide support)

https://github.com/grpc/grpc/blob/master/doc/keepalive.md

### Fine-grained Transport Control: RPC Wait-for-ready

Grpc.Core: supported, provided by C-core

grpc-dotnet: Supported on .NET 5 or later

https://github.com/grpc/grpc/blob/master/doc/wait-for-ready.md

### Naming / Resolver API

Grpc.Core: conforms with the spec thanks to the C-core dependency. It is currently not possible to provide custom C# resolvers via resolver API (the APIs are in C core and aren't exposed in the C# layer).

grpc-dotnet: Resolving is fully supported on .NET 5 or later. grpc-dotnet also provides an [API to write custom resolvers](https://learn.microsoft.com/aspnet/core/grpc/loadbalancingwrite-custom-resolvers-and-load-balancers).

https://github.com/grpc/grpc/blob/master/doc/naming.md

### Port sharing (gRPC and HTTP traffic on the same port)

grpc-dotnet allows port-sharing: serving both gRPC and non-gRPC traffic by the same server (Grpc.Core doesn't support)

## Add-on Features

Features that don't necessarily require changes to the implementation's internals. They usually come as a separate opt-in nuget package.

### Addon: Server Reflection
Grpc.Core: supported via Grpc.Reflection nuget package

grpc-dotnet: supported via Grpc.Reflection nuget package and Grpc.AspNetCore.Server.Reflection helper.

https://github.com/grpc/grpc/blob/master/doc/server-reflection.md

### Addon: Health checking
Both implementations provide support via the Grpc.HealthCheck nuget package

Note: Slightly orthogonal, but when deployed to kubernetes environment, grpc-dotnet is in slightly better position to respond to native kubernetes health check requests (which come as HTTP1.1 requests) because ASP.NET core can also serve HTTP requests. See for context: https://kubernetes.io/blog/2018/10/01/health-checking-grpc-servers-on-kubernetes/

https://github.com/grpc/grpc/blob/master/doc/health-checking.md

### Addon: Rich error model (status.proto)

Bindings for idiomatic integration of RPC with the "Rich error model" based on the stadard `status.proto`.
Currently not implemented for any of the implementations.

https://cloud.google.com/apis/design/errors#error_model

### Addon: `HttpClientFactory` integration

grpc-dotnet supports integration with `HttpClientFactory` via the Grpc.Net.ClientFactory package. Client factory integration offers:

- Central configuration of gRPC clients.
- Inject clients into your application with .NET dependency injection.
- Reuse of channel instances.
- Automatic propagation of cancellation and deadline when used in a `Grpc.AspNetCore` hosted gRPC service.

### Addon: gRPC JSON transcoding

grpc-dotnet supports providing a RESTful JSON API using [gRPC JSON transcoding](https://learn.microsoft.com/aspnet/core/grpc/json-transcoding).

gRPC JSON transcoding is an extension for ASP.NET Core that creates RESTful JSON APIs for gRPC services. Once configured, transcoding allows apps to call gRPC services with familiar HTTP concepts:

- HTTP verbs
- URL parameter binding
- JSON requests/responses

gRPC can still be used to call services.

### Addon: gRPC-Web

grpc-dotnet supports the [gRPC-Web protocol](https://github.com/grpc/grpc/blob/master/doc/PROTOCOL-WEB.md).

gRPC-Web allows browser JavaScript and Blazor apps to call gRPC services. It's not possible to call a gRPC service over HTTP/2 from a browser-based app. gRPC services hosted in ASP.NET Core can be configured to support gRPC-Web alongside gRPC over HTTP/2.

https://learn.microsoft.com/aspnet/core/grpc/grpcweb
