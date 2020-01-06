# gRPC-dotnet future planning (for ASP.NET Core 5.0)

## Schedule and versioning

The work described here targets the ASP.NET Core 5.0 release which is currently scheduled for November 2020: https://github.com/dotnet/core/blob/master/roadmap.md. The version of the gRPC-dotnet release will follow the rules laid out in https://github.com/grpc/grpc-dotnet/blob/master/doc/versioning.md.

## Runtime

- Linker-friendly gRPC
  - As part of positioning .NET as a viable technology for cloud native, we should ensure we are linker-friendly and audit any possible future usage of reflection/ref-emit
  - Optimize for working-set memory/published output size
    - See https://github.com/rynowak/link-a-thon/blob/master/Findings.md for comparison

  - Additional Server
    - HttpSysServer
    - IIS In-proc

- Introduce additional transports on the server
  - Unix-domain sockets
  - Named pipes (on Windows)
  - [Stretch] QUIC

- Introduce additional transports on the client
  - Unix-domain sockets
  - Named pipes (on Windows)
  - [Stretch] QUIC

- Serializer/de-serializer performance improvements
  - https://github.com/protocolbuffers/protobuf/pull/5888

- HTTP/2 performance on the server
  - HPACK Static dictionary
  - HPACK Dynamic dictionary
  - Pooling of HTTP/2 Streams
  - [Stretch] Window update tuning

 - Ensure code-first gRPC is successful
  - Marc Gravell is using protobuf.net to build a code-first gRPC experience on grpc-dotnet. Ensure our abstractions allow him to be successful.

 - Ensure WCF to gRPC ebook is successful
  - Provide SME guidance where applicable

- Testing existing client libraries on both implementations
    - There are APIs and SDKs to share between the implementations
    - https://github.com/grpc/grpc-dotnet/blob/master/doc/implementation_comparison.md
    - Here is the list of client libraries that grpc-dotnet should work with
        - https://github.com/googleapis/google-cloud-dotnet
        - there's an add-on library https://github.com/GoogleCloudPlatform/grpc-gcp-csharp that is used to provide some of the functionality (like channel affinity) for some of the APIs.

It would also be useful to help with figuring out a way to make the client libraries not depend on Grpc.Core directly (which is currently the case).

- Load balancing
    -	Integration with XDS load balancing APIs
    -	May need Connectivity APIs work in HttpClient
    - We should start with supporting round_robin loadbalancing as a first step. The features needed for that are very likely to be needed for XDS loadbalancing as well.


- Connection Features
    -	Keep alive, without keepalive support it can be difficult to have long-lived streaming call that only rarely sends messages (e.g. "client subscribes for receiving updates from server " scenario)
    -	Connection idle: https://github.com/grpc/grpc/blob/master/doc/connection-backoff.md
    -	Wait for ready
    -	These features are often needed for production ready services

- Support status.proto
    - Trailing rich metadata for error details
    - Works across different implementation
    - For details, see "Richer error model" here: https://grpc.io/docs/guides/error/ and also https://cloud.google.com/apis/design/errors#error_model
example what we've done for e.g. python: https://github.com/grpc/proposal/blob/master/L44-python-rich-status.md
the expectation is that we'll ship this as a separate nuget (e.g. "Grpc.StatusProto")


- Channelz support
    -	Observability of server status

- gRPC web

## Tooling

- [Stretch] Build a LSP for protobuf
- [Stretch] VS Code extension to consume LSP
  - [More Info Needed] Possibly partner with VS Code team here
- [Stretch] VS Extension to consume LSP

- Tooling for working with gRPC services without building a client project
  - Integration with HttpREPL or something like WCFTestClient?

- Generating and working with client certificates

- Publishing to AKS

## Ecosystem

- Envoy integration
  - [Stretch] Integration with Envoy XDS APIs for client-side load balancing
- Kubernetes/AKS
  - [More Info Needed] Helm charts for CLI publish
- Xamarin apps support

## Documentation/Samples

- OAuth E2E
- More AuthN/AuthZ coverage
- gRPC specific features
  - Deadlines
  - Cancellation
- Distributed Tracing
  - OpenCenus/Open Telemetry
- Interceptors

## Intentional cuts/omissions

- gRPC with JSON (as opposed to protobuf)
